namespace AudiobookRequest.Api.Services;

public static class ExceptionText
{
    /// <summary>
    /// An error as shown to a person on a request. A bare "Object reference not set to an instance of an object" says nothing,
    /// so for programming errors the place it came from is added, which is what is needed to find and fix it.
    /// </summary>
    public static string Describe(Exception ex)
    {
        if (ex is NullReferenceException or ArgumentNullException or InvalidCastException or IndexOutOfRangeException)
        {
            var site = ex.TargetSite is { } m ? $"{m.DeclaringType?.Name}.{m.Name}" : "unknown location";
            return $"{ex.Message} (internal error in {site}; the log has the details)";
        }
        return ex.Message;
    }
}
