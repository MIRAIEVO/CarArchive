#if DEBUG
using System.Text.Json;
using CarArchive.Core;
using CarArchive.Services;
using Microsoft.Data.Sqlite;

namespace CarArchive;

public sealed partial class MainPage
{
    // Explicit opt-in integration probe; never reads or writes the active production workspace.
    private async Task RunLiveVerificationAsync(string directory)
    {
        directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
        var output=Path.Combine(directory,"live-verification.json");var calls=0;string? original=null;string? originalHash=null;
        RecognitionResult? identified=null;VerificationReport? verification=null;
        bool? OriginalUnchanged()
        {
            if(original is null || originalHash is null || !File.Exists(original)) return null;
            using var stream=File.OpenRead(original);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).Equals(originalHash,StringComparison.OrdinalIgnoreCase);
        }
        void Write(string status,string? error=null)=>File.WriteAllText(output,JsonSerializer.Serialize(new {
            Status=status,RealApiCalls=calls,Error=error,Identification=identified,Verification=verification,
            OriginalUnchanged=OriginalUnchanged(),
            CompletedUtc=DateTimeOffset.UtcNow
        },new JsonSerializerOptions(StateStore.Json) { WriteIndented=true }));
        try
        {
            if(!ViewModels.MainViewModel.DataDirectory.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || VM.Groups.Count!=0)
                throw new InvalidOperationException("Live verification requires an empty isolated workspace.");
            var source=Environment.GetEnvironmentVariable("CARARCHIVE_LIVE_SOURCE_DATA");
            original=Environment.GetEnvironmentVariable("CARARCHIVE_LIVE_PHOTO");
            if(string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(original)) throw new InvalidOperationException("Explicit live-test source and sample are required.");
            originalHash=await ArchiveService.HashAsync(original);
            var settings=new AppSettings();
            using(var database=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path.Combine(source,"cararchive.db"),Mode=SqliteOpenMode.ReadOnly }.ToString()))
            {
                database.Open();using var query=database.CreateCommand();query.CommandText="SELECT json FROM workspace WHERE id=1";
                if(query.ExecuteScalar() is string json)
                { using var document=JsonDocument.Parse(json);if(document.RootElement.TryGetProperty("Settings",out var stored)) settings=stored.Deserialize<AppSettings>(StateStore.Json) ?? settings; }
            }
            settings.Mode=RecognitionMode.Fast;
            var key=new CredentialService(source).Read();if(key.Length==0) throw new InvalidOperationException("No saved API credential is available for the current Windows user.");
            var copy=Path.Combine(directory,"sample"+Path.GetExtension(original));File.Copy(original,copy,false);
            var photo=await VM.Images.ImportAsync(copy,default);
            if(Environment.GetEnvironmentVariable("CARARCHIVE_LIVE_CROP") is { Length:>0 } crop) photo.Crop=JsonSerializer.Deserialize<CropRect>(crop,StateStore.Json);
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(9));
            using var http=new HttpClient { Timeout=TimeSpan.FromMinutes(3) };
            var service=new GeminiService(http,VM.Images,()=>key,()=> { if(calls>=4) throw new InvalidOperationException("Live verification call limit reached.");calls++;Write("Running"); });
            Write("Running");identified=await service.RecognizeAsync([photo],settings,false,timeout.Token);Write("IdentificationCompleted");
            if(identified.Identity.Series.Length>0 || identified.Identity.Variant.Length>0)
                verification=await service.VerifyAsync([photo],settings,identified.Identity,identified.Uncertainties,timeout.Token);
            Write(verification?.Status is VerificationStatus.Incomplete or VerificationStatus.Canceled?"Incomplete":"Completed");
            App.MainWindow!.Title="CarArchive · 受限联网验证完成";
        }
        catch(Exception error)
        {
            // Provider errors are already sanitized by GeminiService; never serialize request bodies or credentials.
            Write("Failed",error is System.Security.Cryptography.CryptographicException?"The saved credential belongs to another Windows user.":error.Message);
            VM.Status="受限联网验证未完成；请查看隔离报告。";
        }
    }
}
#endif
