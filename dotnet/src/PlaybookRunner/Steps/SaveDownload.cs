// Save a download safely: write to "<file>.part", then rename. An interrupted run
// never leaves a half-written CSV that skipIfExists would later trust.

using Microsoft.Playwright;

namespace PlaybookRunner.Steps;

public static class SaveDownload
{
    public static async Task<string> SaveAsync(IDownload dl, string file)
    {
        var output = Path.GetFullPath(file);
        var part = $"{output}.part";
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        try
        {
            await dl.SaveAsAsync(part);
            File.Move(part, output, overwrite: true);
        }
        catch
        {
            File.Delete(part);
            throw;
        }
        return output;
    }
}
