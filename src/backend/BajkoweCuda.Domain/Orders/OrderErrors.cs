namespace BajkoweCuda.Domain.Orders;

/// <summary>Error codes of the order rules. Keep in step with the frontend's translations.</summary>
public static class OrderErrors
{
    /// <summary>The status does not allow this step (for example "ship" a draft). The API answers 409.</summary>
    public const string InvalidTransition = "orders.invalid_transition";

    /// <summary>The field cannot be changed in this status (price after payment, anything after the end). 409.</summary>
    public const string NotEditable = "orders.not_editable";

    /// <summary>"Ready" needs at least one photo of the finished toy. 409.</summary>
    public const string PhotoRequired = "orders.photo_required";

    /// <summary>Photos can be added and removed only while the toy is being made or is ready. 409.</summary>
    public const string PhotosLocked = "orders.photos_locked";

    public const string ReasonRequired = "orders.reason_required";
    public const string TitleRequired = "orders.title_required";
    public const string TooLong = "orders.too_long";
    public const string InvalidPrice = "orders.invalid_price";
    public const string TooManyPhotos = "orders.too_many_photos";
    public const string PhotoNotFound = "orders.photo_not_found";
}
