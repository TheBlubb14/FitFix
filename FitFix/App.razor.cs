using Dynastream.Fit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace FitFix;

partial class App
{
    private string subSport = "Mountain";
    private bool keepFileName = true;
    private string newFileName = "Tour";
    private bool processing = false;

    private async void Upload(InputFileChangeEventArgs e)
    {
        processing = true;
        StateHasChanged();

        try
        {
            if (!Enum.TryParse<SubSport>(subSport, out var newSubSport))
            {
                Console.WriteLine($"Could not parse SubSport: {subSport}");
                return;
            }

            if (e.FileCount == 0)
            {
                Console.WriteLine("Must select a file");
                return;
            }

            using var stream = e.File.OpenReadStream();
            using var fit = new MemoryStream();
            await stream.CopyToAsync(fit);
            fit.Seek(0, SeekOrigin.Begin);

            var decoder = new Decode();

            if (!decoder.IsFIT(fit))
            {
                Console.WriteLine("not .fit file");
                return;
            }

            var listener = new FitListener();
            decoder.MesgEvent += listener.OnMesg;
            var mesgs = new List<Mesg>();
            decoder.MesgEvent += (s, e) =>
            {
                mesgs.Add(e.mesg);
            };
            bool readOK = decoder.Read(fit);

            var fileId = listener.FitMessages.FileIdMesgs.First();

            Console.WriteLine($"Manufacturer: {fileId.GetManufacturer()} ProductName: {fileId.GetProductNameAsString()} SerialNumber: {fileId.GetSerialNumber()}");

            var session = listener.FitMessages.SessionMesgs.First();
            var sessionSport = session.GetSport();
            var sessionSubSport = session.GetSubSport();

            var sport = listener.FitMessages.SportMesgs.First();
            var sportSport = sport.GetSport();
            var sportSubSport = sport.GetSubSport();

            Console.WriteLine($"Session: {sessionSport} - {sessionSubSport}");
            Console.WriteLine($"Sport: {sportSport} - {sportSubSport}- {sport.GetNameAsString()}");

            if (sessionSport != Sport.Cycling || sessionSubSport == newSubSport)
            {
                Console.WriteLine("Not cycling, or sub sport already correct");
                return;
            }

            Console.WriteLine($"Detected default iGPSport mode. Change to {newSubSport}");

            using var newFit = new MemoryStream();
            var header = new Header(fit);

            var major = header.ProtocolVersion >> 4;
            var minor = header.ProtocolVersion & 0xF;
            ProtocolVersion protocolVersion;
            if (major == 1 && minor == 0)
                protocolVersion = ProtocolVersion.V10;
            else if (major == 2 && minor == 0)
                protocolVersion = ProtocolVersion.V20;
            else
            {
                Console.WriteLine($"ProtocolVersion Major: {major} Minor: {minor} not supported!");
                return;
            }

            var encode = new Encode(newFit, protocolVersion);

            // Find session by num. Num logic is defined in FitListener, so we compare with the session from FitListener.
            var originalSession = mesgs.FirstOrDefault(x => x.Num == session.Num);
            originalSession?.SetFieldValue("SubSport", newSubSport);

            var originalSport = mesgs.FirstOrDefault(x => x.Num == sport.Num);
            originalSport?.SetFieldValue("SubSport", newSubSport);

            encode.Write(mesgs);
            encode.Close();

            newFit.Seek(0, SeekOrigin.Begin);
            using var streamRef = new DotNetStreamReference(stream: newFit);
            var fileName = keepFileName ? e.File.Name : string.IsNullOrWhiteSpace(newFileName) ? e.File.Name : string.IsNullOrWhiteSpace(Path.GetExtension(newFileName)) ? $"{newFileName}.fit" : newFileName;
            await JS.InvokeVoidAsync("downloadFileFromStream", fileName, streamRef);
        }
        finally
        {
            processing = false;
            StateHasChanged();
        }
    }
}
