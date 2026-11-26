using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Google.Apis.Upload;

namespace Clipper98;

public sealed class YouTubeUploadService
{
    private static readonly string[] Scopes = [YouTubeService.Scope.YoutubeUpload];

    public async Task<YouTubeService> ConnectAsync(string clientSecretsJson, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(clientSecretsJson));
        var secrets = GoogleClientSecrets.FromStream(stream).Secrets;
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            Scopes,
            "user",
            cancellationToken,
            new ProtectedYouTubeTokenStore());

        return new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Clipper98"
        });
    }

    public async Task<Video> UploadScheduledAsync(
        YouTubeService service,
        YouTubeUploadItem item,
        IProgress<IUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(item.FilePath))
            throw new FileNotFoundException("The queued video file could not be found.", item.FilePath);
        if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 100)
            throw new InvalidOperationException("Enter a video title between 1 and 100 characters.");
        if (item.Description.Length > 5000)
            throw new InvalidOperationException("The video description cannot exceed 5,000 characters.");
        if (item.PublishAt is not { } publishAt || publishAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Set a future publish time before uploading this video.");

        var video = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = item.Title.Trim(),
                Description = item.Description.Trim()
            },
            Status = new VideoStatus
            {
                PrivacyStatus = "private",
                PublishAtDateTimeOffset = publishAt.ToUniversalTime(),
                SelfDeclaredMadeForKids = false
            }
        };

        await using var file = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var request = service.Videos.Insert(video, "snippet,status", file, "video/mp4");
        Video? responseVideo = null;
        request.ProgressChanged += update => progress?.Report(update);
        request.ResponseReceived += uploaded => responseVideo = uploaded;
        var uploadResult = await request.UploadAsync(cancellationToken);
        if (uploadResult.Status == UploadStatus.Failed)
            throw new YouTubeUploadOutcomeUnknownException(
                $"Google API upload status: {uploadResult.Status}. {uploadResult.Exception?.ToString() ?? "No additional error details were returned."}");
        if (uploadResult.Status != UploadStatus.Completed)
            throw new YouTubeUploadOutcomeUnknownException(
                $"Google API upload status: {uploadResult.Status}. Check the process log for transfer progress.");

        return responseVideo ?? request.ResponseBody
            ?? throw new YouTubeUploadOutcomeUnknownException(
                "Google API reported a completed transfer but returned no video record.");
    }
}

public sealed class YouTubeUploadOutcomeUnknownException : Exception
{
    public YouTubeUploadOutcomeUnknownException(string details)
        : base($"The upload result could not be confirmed. {details} Check YouTube Studio before retrying to avoid a duplicate upload.")
    {
    }
}
