using QPlayer.SourceGenerator;
using QPlayer.ViewModels;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.TcpCuePlugin
{

    [Model(typeof(TcpCueModel))]
    [GenerateView]
    [View(typeof(TcpCueViewModelView))]
    [DisplayName("TCP Cue")]
    [Icon("IconTcpCue", typeof(Icons))]
    public partial class TcpCueViewModel : CueViewModel
    {

        [Reactive/*, ChangesProp(nameof(OSCMessageValid))*/]
        [Tooltip("The TCP Host of the Targer in the form '192.168.0.123'.")]
        private string host = "192.168.0.123";

        [Reactive/*, ChangesProp(nameof(OSCMessageValid))*/]
        [Tooltip("The TCP Port.")]
        private string port = "80";

        [Reactive/*, ChangesProp(nameof(OSCMessageValid))*/]
        [Tooltip("The Request Path")]
        private string path = "/";


        public TcpCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
        { }

        public override async void Go()
        {
            base.Go();
            await TcpRequest();
        }

        private async Task TcpRequest()
        {
            if (!int.TryParse(port, out int portnumber))
            {
                MainViewModel.Log($"Failed to send TCP message. The Port is invalid: {port}", MainViewModel.LogLevel.Error);
                return;
            }

            try
            {
                using var tcpClient = new TcpClient(host, portnumber);
                //await tcpClient.ConnectAsync(host, portnumber);
                using var stream = tcpClient.GetStream();

                tcpClient.ReceiveTimeout = 2000;

                //string http09Request = $"GET {path} HTTP/0.9\r\n";
                string http09Request = $"GET /\r\n";
                byte[] requestBytes = Encoding.ASCII.GetBytes(http09Request);

                stream.Write(requestBytes, 0, requestBytes.Length);
                stream.Flush();



                using MemoryStream ms = new MemoryStream();
                byte[] buffer = new byte[4096];
                int bytesRead;

                try
                {
                    // Liest so lange, bis der Server die Verbindung trennt (bytesRead == 0)
                    // ODER bis das Timeout-Limit erreicht wird (wirft eine IOException)
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, bytesRead);
                    }
                }
                catch (IOException ex) when (ex.InnerException is SocketException sx && sx.SocketErrorCode == SocketError.TimedOut)
                {
                    // Timeout erreicht – wir nehmen an, dass alle Daten da sind
                    Console.WriteLine("-> Lese-Timeout erreicht. Verbindung wird lokal geschlossen.");
                }

                // Auswertung der empfangenen Bytes
                string response = Encoding.UTF8.GetString(ms.ToArray());



                Console.WriteLine(response);

            }
            catch (Exception ex)
            {
                MainViewModel.Log($"Failed to send TCP message: {ex.Message}", MainViewModel.LogLevel.Error);
            }
        }


    }
}
