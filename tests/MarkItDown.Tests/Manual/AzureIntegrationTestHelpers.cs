namespace MarkItDown.Tests.Manual;

internal static class AzureIntegrationResourceParser
{
    public static (string? SubscriptionId, string? ResourceGroup, string? AccountName) FromResourceId(string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return (null, null, null);
        }

        var trimmed = resourceId.Trim('/');
        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);

        string? subscriptionId = null;
        string? resourceGroup = null;
        string? accountName = null;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Equals("subscriptions", StringComparison.OrdinalIgnoreCase) && i + 1 < segments.Length)
            {
                subscriptionId ??= segments[i + 1];
            }
            else if (segment.Equals("resourcegroups", StringComparison.OrdinalIgnoreCase) && i + 1 < segments.Length)
            {
                resourceGroup ??= segments[i + 1];
            }
            else if (segment.Equals("accounts", StringComparison.OrdinalIgnoreCase) && i + 1 < segments.Length)
            {
                accountName ??= segments[i + 1];
            }
        }

        return (subscriptionId, resourceGroup, accountName);
    }
}
