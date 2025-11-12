using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Google.Apis.Auth.OAuth2;

namespace MarkItDown.Intelligence.Providers.Google;

internal static class GoogleCredentialResolver
{
    private static readonly Type GoogleCredentialInterfaceType = typeof(GoogleCredential)
        .Assembly
        .GetType("Google.Apis.Auth.OAuth2.IGoogleCredential", throwOnError: true)!;

    private static readonly MethodInfo FromJsonGenericDefinition = typeof(CredentialFactory)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(CredentialFactory.FromJson) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

    private static readonly MethodInfo FromFileGenericDefinition = typeof(CredentialFactory)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(CredentialFactory.FromFile) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

    private static readonly MethodInfo FromJsonConcrete = FromJsonGenericDefinition.MakeGenericMethod(GoogleCredentialInterfaceType);
    private static readonly MethodInfo FromFileConcrete = FromFileGenericDefinition.MakeGenericMethod(GoogleCredentialInterfaceType);

    private static readonly MethodInfo ToGoogleCredentialMethod = GoogleCredentialInterfaceType
        .GetMethod("ToGoogleCredential", BindingFlags.Public | BindingFlags.Instance)!;

    public static GoogleCredential? Resolve(
        GoogleCredential? explicitCredential,
        string? jsonCredentials,
        string? credentialsPath,
        IReadOnlyList<string>? scopes)
    {
        var credential = explicitCredential
            ?? (!string.IsNullOrWhiteSpace(jsonCredentials) ? CreateFromJson(jsonCredentials) : null)
            ?? (!string.IsNullOrWhiteSpace(credentialsPath) ? CreateFromFile(credentialsPath) : null);

        if (credential is null)
        {
            return null;
        }

        if (scopes is { Count: > 0 } && credential.IsCreateScopedRequired)
        {
            credential = credential.CreateScoped(scopes);
        }

        return credential;
    }

    private static GoogleCredential CreateFromJson(string json)
    {
        var result = FromJsonConcrete.Invoke(null, new object[] { json }) ?? throw new InvalidOperationException("Failed to parse Google credentials JSON.");
        return ConvertToGoogleCredential(result);
    }

    private static GoogleCredential CreateFromFile(string path)
    {
        var result = FromFileConcrete.Invoke(null, new object[] { path }) ?? throw new InvalidOperationException("Failed to load Google credential file.");
        return ConvertToGoogleCredential(result);
    }

    private static GoogleCredential ConvertToGoogleCredential(object instance)
    {
        var credential = ToGoogleCredentialMethod.Invoke(instance, Array.Empty<object>());
        return credential as GoogleCredential ?? throw new InvalidOperationException("Unable to convert Google credential.");
    }
}
