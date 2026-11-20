using BajkoweCuda.Domain.Orders;

namespace BajkoweCuda.Domain.UnitTests.Orders;

/// <summary>Every allowed and forbidden transition, and the edit rules per status (docs/PLAN.md §5, §13).</summary>
public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.NewGuid();

    public enum OrderStep
    {
        Confirm,
        MarkPaid,
        MarkReady,
        MarkShipped,
        Complete,
        Cancel,
    }

    /// <summary>The only status each step starts from, and where it leads (cancel: from any non-final status).</summary>
    private static readonly Dictionary<OrderStep, (OrderStatus From, OrderStatus To)> Allowed = new()
    {
        [OrderStep.Confirm] = (OrderStatus.Draft, OrderStatus.AwaitingPayment),
        [OrderStep.MarkPaid] = (OrderStatus.AwaitingPayment, OrderStatus.InProgress),
        [OrderStep.MarkReady] = (OrderStatus.InProgress, OrderStatus.Ready),
        [OrderStep.MarkShipped] = (OrderStatus.Ready, OrderStatus.Shipped),
        [OrderStep.Complete] = (OrderStatus.Shipped, OrderStatus.Completed),
    };

    public static TheoryData<OrderStatus, OrderStep> EveryStatusAndStep()
    {
        var data = new TheoryData<OrderStatus, OrderStep>();
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            foreach (var step in Enum.GetValues<OrderStep>())
            {
                data.Add(status, step);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryStatusAndStep))]
    public void Each_step_is_allowed_from_its_status_only(OrderStatus status, OrderStep step)
    {
        var order = OrderIn(status);
        var changesBefore = order.StatusChanges.Count;

        var allowed = step == OrderStep.Cancel
            ? status is not (OrderStatus.Completed or OrderStatus.Cancelled)
            : Allowed[step].From == status;

        if (allowed)
        {
            var change = Apply(order, step);
            var expected = step == OrderStep.Cancel ? OrderStatus.Cancelled : Allowed[step].To;
            order.Status.ShouldBe(expected);
            change.FromStatus.ShouldBe(status);
            change.ToStatus.ShouldBe(expected);
            change.ChangedById.ShouldBe(Admin);
            order.StatusChanges.Count.ShouldBe(changesBefore + 1);
        }
        else
        {
            Should.Throw<DomainException>(() => Apply(order, step)).Code.ShouldBe(OrderErrors.InvalidTransition);
            order.Status.ShouldBe(status);
            order.StatusChanges.Count.ShouldBe(changesBefore);
        }
    }

    [Fact]
    public void A_new_order_is_an_invisible_draft_with_a_first_timeline_entry()
    {
        var order = Draft();

        order.Status.ShouldBe(OrderStatus.Draft);
        order.IsVisibleToCustomer.ShouldBeFalse();
        order.Id.Version.ShouldBe(7);
        order.StatusChanges.ShouldHaveSingleItem().FromStatus.ShouldBeNull();
        order.Title.ShouldBe("Fox with a scarf");
    }

    [Fact]
    public void Confirming_makes_it_visible_and_records_the_times_of_each_step()
    {
        var order = Draft();

        order.Confirm(Admin, Now.AddHours(1));
        order.IsVisibleToCustomer.ShouldBeTrue();
        order.ConfirmedAt.ShouldBe(Now.AddHours(1));

        order.MarkPaid(Admin, Now.AddHours(2));
        order.PaidAt.ShouldBe(Now.AddHours(2));
        order.AddPhoto(Guid.NewGuid(), Now);
        order.MarkReady(Admin, Now.AddHours(3));
        order.ReadyAt.ShouldBe(Now.AddHours(3));
        order.MarkShipped(Admin, Now.AddHours(4));
        order.ShippedAt.ShouldBe(Now.AddHours(4));
        order.Complete(Admin, Now.AddHours(5));
        order.CompletedAt.ShouldBe(Now.AddHours(5));
        order.StatusChanges.Select(change => change.ToStatus).ShouldBe(
            [OrderStatus.Draft, OrderStatus.AwaitingPayment, OrderStatus.InProgress, OrderStatus.Ready, OrderStatus.Shipped, OrderStatus.Completed]);
    }

    [Fact]
    public void Ready_needs_a_photo_of_the_finished_toy()
    {
        var order = Draft();
        order.Confirm(Admin, Now);
        order.MarkPaid(Admin, Now);

        Should.Throw<DomainException>(() => order.MarkReady(Admin, Now)).Code.ShouldBe(OrderErrors.PhotoRequired);
        order.Status.ShouldBe(OrderStatus.InProgress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancelling_needs_a_reason(string? reason)
    {
        Should.Throw<DomainException>(() => Draft().Cancel(reason!, Admin, Now)).Code.ShouldBe(OrderErrors.ReasonRequired);
    }

    [Fact]
    public void The_cancellation_reason_is_kept_for_the_customer()
    {
        var order = Draft();
        order.Confirm(Admin, Now);

        var change = order.Cancel("  Fabric is not available  ", Admin, Now.AddDays(1));

        order.CancellationReason.ShouldBe("Fabric is not available");
        order.CancelledAt.ShouldBe(Now.AddDays(1));
        change.Comment.ShouldBe("Fabric is not available");
    }

    public static TheoryData<OrderStatus> EveryStatus() => [.. Enum.GetValues<OrderStatus>()];

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void Price_reference_image_title_and_description_change_only_before_payment(OrderStatus status)
    {
        var order = OrderIn(status);
        var editable = status is OrderStatus.Draft or OrderStatus.AwaitingPayment;
        var image = Guid.NewGuid();

        Action[] edits =
        [
            () => order.ChangePrice(Money.Euros(150)),
            () => order.ChangeReferenceImage(image),
            () => order.EditText("Bigger fox", "With boots"),
        ];

        foreach (var edit in edits)
        {
            if (editable)
            {
                edit();
            }
            else
            {
                Should.Throw<DomainException>(edit).Code.ShouldBe(OrderErrors.NotEditable);
            }
        }

        if (editable)
        {
            order.Price.ShouldBe(Money.Euros(150));
            order.ReferenceImageId.ShouldBe(image);
            order.Title.ShouldBe("Bigger fox");
        }
    }

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void The_internal_note_changes_until_the_order_has_ended(OrderStatus status)
    {
        var order = OrderIn(status);

        if (status is OrderStatus.Completed or OrderStatus.Cancelled)
        {
            Should.Throw<DomainException>(() => order.ChangeAdminNote("x")).Code.ShouldBe(OrderErrors.NotEditable);
        }
        else
        {
            order.ChangeAdminNote("  Uses the green felt  ");
            order.AdminNote.ShouldBe("Uses the green felt");
        }
    }

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void Photos_are_managed_while_the_toy_is_being_made_or_ready(OrderStatus status)
    {
        var order = OrderIn(status);
        var open = status is OrderStatus.InProgress or OrderStatus.Ready;

        if (open)
        {
            var photo = order.AddPhoto(Guid.NewGuid(), Now);
            order.Photos.ShouldContain(photo);
            order.RemovePhoto(photo.Id);
        }
        else
        {
            Should.Throw<DomainException>(() => order.AddPhoto(Guid.NewGuid(), Now)).Code.ShouldBe(OrderErrors.PhotosLocked);
        }
    }

    [Fact]
    public void A_ready_order_keeps_at_least_one_photo()
    {
        var order = OrderIn(OrderStatus.Ready);

        Should.Throw<DomainException>(() => order.RemovePhoto(order.Photos.Single().Id)).Code.ShouldBe(OrderErrors.PhotoRequired);
    }

    [Fact]
    public void Photos_can_be_reordered_when_every_photo_is_listed_once()
    {
        var order = OrderIn(OrderStatus.InProgress);
        var first = order.AddPhoto(Guid.NewGuid(), Now);
        var second = order.AddPhoto(Guid.NewGuid(), Now);
        var third = order.AddPhoto(Guid.NewGuid(), Now);

        order.ReorderPhotos([third.Id, first.Id, second.Id]);

        order.Photos.Select(photo => photo.Id).ShouldBe([third.Id, first.Id, second.Id]);
        Should.Throw<DomainException>(() => order.ReorderPhotos([first.Id, first.Id, second.Id])).Code.ShouldBe(OrderErrors.PhotoNotFound);
        Should.Throw<DomainException>(() => order.ReorderPhotos([first.Id])).Code.ShouldBe(OrderErrors.PhotoNotFound);
    }

    [Fact]
    public void An_order_has_at_most_20_photos()
    {
        var order = OrderIn(OrderStatus.InProgress);
        for (var i = 0; i < Order.MaxPhotos; i++)
        {
            order.AddPhoto(Guid.NewGuid(), Now);
        }

        Should.Throw<DomainException>(() => order.AddPhoto(Guid.NewGuid(), Now)).Code.ShouldBe(OrderErrors.TooManyPhotos);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_order_needs_a_title(string title)
    {
        Should.Throw<DomainException>(() => Order.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), title, null, Money.Euros(10), null, null, Admin, Now))
            .Code.ShouldBe(OrderErrors.TitleRequired);
    }

    [Fact]
    public void Texts_have_limits()
    {
        Should.Throw<DomainException>(() => Order.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), new string('a', 201), null, Money.Euros(10), null, null, Admin, Now))
            .Code.ShouldBe(OrderErrors.TooLong);
        Should.Throw<DomainException>(() => Order.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "Fox", new string('a', 4001), Money.Euros(10), null, null, Admin, Now))
            .Code.ShouldBe(OrderErrors.TooLong);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10.555")]
    [InlineData("100000000")]
    public void A_price_is_positive_with_at_most_two_decimals(string amount)
    {
        Should.Throw<DomainException>(() => Money.Euros(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)))
            .Code.ShouldBe(OrderErrors.InvalidPrice);
    }

    [Fact]
    public void Prices_are_in_euros()
    {
        var price = Money.Euros(129.9m);

        price.Currency.ShouldBe("EUR");
        price.ToString().ShouldBe("129.90 EUR");
    }

    [Fact]
    public void The_number_is_shown_with_four_digits()
    {
        Order.FormatNumber(12).ShouldBe("BC-0012");
        Order.FormatNumber(12345).ShouldBe("BC-12345");
    }

    private static Order Draft() =>
        Order.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "  Fox with a scarf  ", "Orange, 30 cm", Money.Euros(120), null, null, Admin, Now);

    /// <summary>An order brought to <paramref name="status"/> by the allowed steps (with a photo where needed).</summary>
    private static Order OrderIn(OrderStatus status)
    {
        var order = Draft();
        if (status == OrderStatus.Draft)
        {
            return order;
        }

        if (status == OrderStatus.Cancelled)
        {
            order.Cancel("No longer wanted", Admin, Now);
            return order;
        }

        order.Confirm(Admin, Now);
        if (status == OrderStatus.AwaitingPayment)
        {
            return order;
        }

        order.MarkPaid(Admin, Now);
        if (status == OrderStatus.InProgress)
        {
            return order;
        }

        order.AddPhoto(Guid.NewGuid(), Now);
        order.MarkReady(Admin, Now);
        if (status == OrderStatus.Ready)
        {
            return order;
        }

        order.MarkShipped(Admin, Now);
        if (status == OrderStatus.Shipped)
        {
            return order;
        }

        order.Complete(Admin, Now);
        return order;
    }

    private static OrderStatusChange Apply(Order order, OrderStep step)
    {
        if (step == OrderStep.MarkReady && order.Status == OrderStatus.InProgress)
        {
            order.AddPhoto(Guid.NewGuid(), Now);
        }

        return step switch
        {
            OrderStep.Confirm => order.Confirm(Admin, Now),
            OrderStep.MarkPaid => order.MarkPaid(Admin, Now),
            OrderStep.MarkReady => order.MarkReady(Admin, Now),
            OrderStep.MarkShipped => order.MarkShipped(Admin, Now),
            OrderStep.Complete => order.Complete(Admin, Now),
            _ => order.Cancel("Changed my mind", Admin, Now),
        };
    }
}
