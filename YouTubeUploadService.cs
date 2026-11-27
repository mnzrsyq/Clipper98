using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Google.Apis.Upload;
using Google.Apis.Requests;

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

        IUploadProgress uploadResult;
        try
        {
            uploadResult = await request.UploadAsync(cancellationToken);
        }
        catch (Google.GoogleApiException apiEx) when (IsUploadLimitExceeded(apiEx))
        {
            throw new YouTubeUploadLimitExceededException();
        }

        if (uploadResult.Status == UploadStatus.Failed)
        {
            if (uploadResult.Exception is Google.GoogleApiException failApiEx && IsUploadLimitExceeded(failApiEx))
                throw new YouTubeUploadLimitExceededException();
            throw new YouTubeUploadOutcomeUnknownException(
                $"Google API upload status: {uploadResult.Status}. {uploadResult.Exception?.ToString() ?? "No additional error details were returned."}");
        }
        if (uploadResult.Status != UploadStatus.Completed)
            throw new YouTubeUploadOutcomeUnknownException(
                $"Google API upload status: {uploadResult.Status}. Check the process log for transfer progress.");

        var finalVideo = responseVideo ?? request.ResponseBody
            ?? throw new YouTubeUploadOutcomeUnknownException(
                "Google API reported a completed transfer but returned no video record.");

        if (!string.IsNullOrWhiteSpace(finalVideo.Id))
        {
            await TryUploadThumbnailAsync(service, finalVideo.Id, item.FilePath, cancellationToken);
        }

        return finalVideo;
    }

    private static async Task TryUploadThumbnailAsync(
        YouTubeService service,
        string videoId,
        string videoFilePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var jpgPath = Path.ChangeExtension(videoFilePath, ".jpg");
            var pngPath = Path.ChangeExtension(videoFilePath, ".png");
            var thumbPath = File.Exists(jpgPath) ? jpgPath : (File.Exists(pngPath) ? pngPath : null);
            if (thumbPath is null) return;

            var mimeType = thumbPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            await using var stream = new FileStream(thumbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var thumbRequest = service.Thumbnails.Set(videoId, stream, mimeType);
            await thumbRequest.UploadAsync(cancellationToken);
        }
        catch
        {
            // Custom thumbnail memerlukan fitur lanjutan/verifikasi nomor HP di YouTube Studio.
            // Jangan gagalkan upload video jika thumbnail ditolak oleh kuota/hak akses channel.
        }
    }

    private static bool IsUploadLimitExceeded(Google.GoogleApiException ex) =>
        ex.Error?.Errors?.Any(e =>
            string.Equals(e.Reason, "uploadLimitExceeded", StringComparison.OrdinalIgnoreCase)) == true;
}

public sealed class YouTubeUploadLimitExceededException : Exception
{
    public YouTubeUploadLimitExceededException()
        : base(
            "YouTube has reached the daily upload limit for this channel. " +
            "No video was uploaded — it is safe to retry tomorrow.\n\n" +
            "To raise this limit, verify your channel with a phone number:\n" +
            "YouTube Studio → Settings → Channel → Feature eligibility → Intermediate features → Verify.")
    {
    }
}

public sealed class YouTubeUploadOutcomeUnknownException : Exception
{
    public YouTubeUploadOutcomeUnknownException(string details)
        : base($"The upload result could not be confirmed. {details} Check YouTube Studio before retrying to avoid a duplicate upload.")
    {
    }
}
