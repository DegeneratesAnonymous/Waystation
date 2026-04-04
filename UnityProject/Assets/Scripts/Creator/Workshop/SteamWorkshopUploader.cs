using System;

namespace Waystation.Creator.Workshop
{
    public enum WorkshopUploadStatus
    {
        Idle,
        Preparing,
        Uploading,
        Success,
        Error
    }

    public class SteamWorkshopUploader
    {
        public WorkshopUploadStatus Status { get; private set; } = WorkshopUploadStatus.Idle;
        public float Progress { get; private set; }
        public string ErrorMessage { get; private set; }

        public event Action<WorkshopUploadStatus> OnStatusChanged;

        public void Upload(string assetDir, string title, string description, string[] tags)
        {
            // Stub: actual Steamworks implementation requires Steamworks.NET
            Status = WorkshopUploadStatus.Preparing;
            OnStatusChanged?.Invoke(Status);

            // In a real implementation, this would:
            // 1. Create/update a UGC item via SteamUGC
            // 2. Set item content to assetDir
            // 3. Submit the update
            // 4. Monitor progress via callback

            UnityEngine.Debug.Log($"[SteamWorkshopUploader] Upload stub called for: {title}");
            Status = WorkshopUploadStatus.Error;
            ErrorMessage = "Steam Workshop integration not yet implemented";
            OnStatusChanged?.Invoke(Status);
        }

        public void Cancel()
        {
            Status = WorkshopUploadStatus.Idle;
            Progress = 0f;
            OnStatusChanged?.Invoke(Status);
        }
    }
}
