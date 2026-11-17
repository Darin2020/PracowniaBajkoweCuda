namespace BajkoweCuda.Api.Observability;

/// <summary>
/// Version of the running build: the git commit the image was built from (the <c>APP_VERSION</c> build argument
/// of the Dockerfile, exposed as an environment variable). <c>dev</c> outside a container.
/// </summary>
internal sealed record AppVersion(string Value)
{
    public const string ConfigurationKey = "APP_VERSION";
    public const string Unknown = "dev";

    public static AppVersion FromConfiguration(IConfiguration configuration)
    {
        var value = configuration[ConfigurationKey];

        return new AppVersion(string.IsNullOrWhiteSpace(value) ? Unknown : value.Trim());
    }
}
