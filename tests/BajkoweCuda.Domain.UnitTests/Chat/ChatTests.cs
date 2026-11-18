using BajkoweCuda.Domain.Chat;

namespace BajkoweCuda.Domain.UnitTests.Chat;

public class ChatTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private static Attachment Upload(Guid owner, DateTimeOffset? at = null) =>
        Attachment.Uploaded(Guid.NewGuid(), owner, "attachments", $"{Guid.NewGuid():N}.webp", $"{Guid.NewGuid():N}.webp", "image/webp", 1234, 800, 600, at ?? Now);

    [Fact]
    public void A_conversation_starts_for_one_customer_with_notifications_off()
    {
        var conversation = Conversation.Start(Customer, Now);

        conversation.CustomerId.ShouldBe(Customer);
        conversation.Id.Version.ShouldBe(7);
        conversation.CustomerEmailNotificationsEnabled.ShouldBeFalse();
        conversation.EmailPromptAnswered.ShouldBeFalse();
        Should.Throw<ArgumentException>(() => Conversation.Start(Guid.Empty, Now));
    }

    [Fact]
    public void A_text_message_is_trimmed_and_read_by_its_own_side()
    {
        var conversation = Conversation.Start(Customer, Now);

        var message = conversation.PostText(ChatSide.Customer, Customer, "  A bear, please  ", [], Now.AddMinutes(1));

        message.Body.ShouldBe("A bear, please");
        message.Kind.ShouldBe(MessageKind.Text);
        message.ConversationId.ShouldBe(conversation.Id);
        message.IsUnreadBy(ChatSide.Customer).ShouldBeFalse();
        message.IsUnreadBy(ChatSide.Studio).ShouldBeTrue();
        conversation.LastMessageAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void A_studio_reply_is_unread_by_the_customer()
    {
        var conversation = Conversation.Start(Customer, Now);

        var message = conversation.PostText(ChatSide.Studio, Admin, "Sure!", [], Now);

        message.SenderId.ShouldBe(Admin);
        message.IsUnreadBy(ChatSide.Customer).ShouldBeTrue();
        message.IsUnreadBy(ChatSide.Studio).ShouldBeFalse();
    }

    [Fact]
    public void Only_the_conversation_customer_writes_on_the_customer_side()
    {
        var conversation = Conversation.Start(Customer, Now);

        Should.Throw<InvalidOperationException>(() => conversation.PostText(ChatSide.Customer, Guid.NewGuid(), "Hi", [], Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_message_needs_text_or_an_image(string? body)
    {
        var conversation = Conversation.Start(Customer, Now);

        var error = Should.Throw<DomainException>(() => conversation.PostText(ChatSide.Customer, Customer, body, [], Now));

        error.Code.ShouldBe(ChatErrors.EmptyMessage);
    }

    [Fact]
    public void A_message_of_images_only_is_fine()
    {
        var conversation = Conversation.Start(Customer, Now);
        var image = Upload(Customer);

        var message = conversation.PostText(ChatSide.Customer, Customer, null, [image], Now);

        message.Body.ShouldBeEmpty();
        image.MessageId.ShouldBe(message.Id);
    }

    [Fact]
    public void A_message_has_at_most_2000_characters()
    {
        var conversation = Conversation.Start(Customer, Now);

        conversation.PostText(ChatSide.Customer, Customer, new string('a', Message.BodyMaxLength), [], Now).Body.Length.ShouldBe(2000);
        Should.Throw<DomainException>(() => conversation.PostText(ChatSide.Customer, Customer, new string('a', 2001), [], Now))
            .Code.ShouldBe(ChatErrors.MessageTooLong);
    }

    [Fact]
    public void A_message_has_at_most_five_images()
    {
        var conversation = Conversation.Start(Customer, Now);
        var images = Enumerable.Range(0, 6).Select(_ => Upload(Customer)).ToList();

        Should.Throw<DomainException>(() => conversation.PostText(ChatSide.Customer, Customer, "Look", images, Now))
            .Code.ShouldBe(ChatErrors.TooManyAttachments);
        images.ShouldAllBe(image => image.MessageId == null);
    }

    [Fact]
    public void Someone_else_s_image_cannot_be_sent()
    {
        var conversation = Conversation.Start(Customer, Now);

        Should.Throw<DomainException>(() => conversation.PostText(ChatSide.Studio, Admin, "Look", [Upload(Customer)], Now))
            .Code.ShouldBe(ChatErrors.AttachmentUnavailable);
    }

    [Fact]
    public void An_image_is_sent_only_once()
    {
        var conversation = Conversation.Start(Customer, Now);
        var image = Upload(Customer);
        conversation.PostText(ChatSide.Customer, Customer, "One", [image], Now);

        Should.Throw<DomainException>(() => conversation.PostText(ChatSide.Customer, Customer, "Two", [image], Now))
            .Code.ShouldBe(ChatErrors.AttachmentUnavailable);
    }

    [Fact]
    public void A_system_message_is_read_by_the_studio_and_moves_the_conversation()
    {
        var conversation = Conversation.Start(Customer, Now);
        var orderId = Guid.NewGuid();

        var message = conversation.PostSystem("order.confirmed", """{"number":"BC-0012"}""", orderId, Now.AddHours(1));

        message.Kind.ShouldBe(MessageKind.System);
        message.SenderId.ShouldBeNull();
        message.From.ShouldBe(ChatSide.Studio);
        message.OrderId.ShouldBe(orderId);
        message.IsUnreadBy(ChatSide.Customer).ShouldBeTrue();
        message.IsUnreadBy(ChatSide.Studio).ShouldBeFalse();
        conversation.LastMessageAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void Reading_keeps_the_first_time()
    {
        var message = Conversation.Start(Customer, Now).PostText(ChatSide.Studio, Admin, "Hi", [], Now);

        message.MarkReadBy(ChatSide.Customer, Now.AddMinutes(5));
        message.MarkReadBy(ChatSide.Customer, Now.AddMinutes(9));

        message.ReadByCustomerAt.ShouldBe(Now.AddMinutes(5));
    }

    [Fact]
    public void Answering_the_email_question_records_the_answer()
    {
        var conversation = Conversation.Start(Customer, Now);

        conversation.SetEmailNotifications(false);

        conversation.EmailPromptAnswered.ShouldBeTrue();
        conversation.CustomerEmailNotificationsEnabled.ShouldBeFalse();

        conversation.SetEmailNotifications(true);
        conversation.CustomerEmailNotificationsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void An_unsent_upload_is_abandoned_after_a_day()
    {
        var image = Upload(Customer, Now);

        image.IsAbandoned(Now.AddHours(23)).ShouldBeFalse();
        image.IsAbandoned(Now.AddHours(24)).ShouldBeTrue();

        Conversation.Start(Customer, Now).PostText(ChatSide.Customer, Customer, null, [image], Now.AddHours(1));
        image.IsAbandoned(Now.AddDays(3)).ShouldBeFalse();
    }
}
