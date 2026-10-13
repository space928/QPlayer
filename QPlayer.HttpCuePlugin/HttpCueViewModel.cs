using QPlayer.SourceGenerator;
using QPlayer.ViewModels;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using static QPlayer.HttpCuePlugin.HttpCueModel;

namespace QPlayer.HttpCuePlugin
{

    [Model(typeof(HttpCueModel))]
    [GenerateView]
    [View(typeof(HttpCueViewModelView))]
    [DisplayName("HTTP Cue")]
    [Icon("IconHttpCue", typeof(Icons))]
    public partial class HttpCueViewModel : CueViewModel
    {

        [Reactive/*, ChangesProp(nameof(OSCMessageValid))*/]
        [Tooltip("The HTTP URI of the Targer in the form 'http://192.168.0.123/api/dosomething'. It can also be HTTPS.")]
        private string url = "http://";

        [Reactive/*, ChangesProp(nameof(OSCMessageValid))*/]
        [Tooltip("The HTTP Method. Allowed are 'Get' and 'Post'")]
        private CueHttpMethod method = CueHttpMethod.Get;


        public HttpCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
        { }

        public override async void Go()
        {
            base.Go();
            await HttpRequest();
        }


        private async Task HttpRequest()
        {
            try
            {
                using var client = new HttpClient();
                client.BaseAddress = new Uri(url);

                HttpMethod m = HttpMethod.Get;
                switch (method)
                {
                    case CueHttpMethod.Get: m = HttpMethod.Get; break;
                    case CueHttpMethod.Post: m = HttpMethod.Post; break;
                }

                var request = new HttpRequestMessage(m, url)
                {
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact
                };

                var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    MainViewModel.Log($"HTTP Sucessful", MainViewModel.LogLevel.Info);
                }
                else
                {
                    MainViewModel.Log($"HTTP Unsuccessful", MainViewModel.LogLevel.Warning);
                }

            }
            catch (Exception ex)
            {
                MainViewModel.Log($"Failed to send HTTP message: {ex.Message}", MainViewModel.LogLevel.Error);
            }
        }

    }
}
