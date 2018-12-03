namespace SenderModule
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using Microsoft.Azure.Devices.Client;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    class Program
    {
        static int counter;
        static IotEdgeConfiguration configuration;
        static System.Threading.Timer timer;
        static ModuleClient ioTHubModuleClient;
        static Sensor sensor = new Sensor();

        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {
            AmqpTransportSettings amqpSetting = new AmqpTransportSettings(TransportType.Amqp_Tcp_Only);
            ITransportSettings[] settings = { amqpSetting };

            // Open a connection to the Edge runtime
            ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

            configuration = IotEdgeConfiguration.CreateFromEnviromentVariables();
            
            Console.WriteLine($"Interval set from ENV to: {configuration.Interval}");
            Console.WriteLine($"Decoder set from ENV to: {configuration.Decoder}");

            timer = new System.Threading.Timer(TimerCallback, null, configuration.Interval, configuration.Interval);
        }

        async static void TimerCallback(object state)
        {
            // Get sensor value
            sensor.Value = sensor.GetValue();

            // Send value to Decoder
            JObject decodedMessage = await DecodeMessage(Encoding.UTF8.GetBytes(sensor.Value.ToString()), 1, configuration.Decoder);

            // Send data upstream.
            var outMessage = new Message(Encoding.UTF8.GetBytes(decodedMessage.ToString()));
            try
            {
                await ioTHubModuleClient.SendEventAsync("output1", outMessage);
                Console.WriteLine("Message sent to output1: " + decodedMessage.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error sending message to output1: " + ex.ToString());
            }
            Console.WriteLine("Done. \n");
        }

        public static async Task<JObject> DecodeMessage(byte[] payload, uint fport, string SensorDecoder)
        {

            string result;
            var base64Payload = Convert.ToBase64String(payload);

            // Call SensorDecoderModule hosted in seperate container ("http://" in SensorDecoder)
            // Format: http://containername/api/decodername
            string toCall = SensorDecoder;

            if (SensorDecoder.EndsWith("/"))
            {
                toCall = SensorDecoder.Substring(0, SensorDecoder.Length - 1);
            }

            // use HttpUtility to UrlEncode Fport and payload
            string fportEncoded = HttpUtility.UrlEncode(fport.ToString());
            string payloadEncoded = HttpUtility.UrlEncode(Encoding.ASCII.GetString(payload));

            // Add Fport and Payload to URL
            toCall = $"{toCall}?fport={fportEncoded}&payload={payloadEncoded}";
            var stopwatch = Stopwatch.StartNew();

            Console.WriteLine($"Calling decoder function: {toCall} via HTTP.");
            // Call SensorDecoderModule
            result = await CallSensorDecoderModule(toCall, payload);

            stopwatch.Stop();
            Console.WriteLine($"Called decoder in {stopwatch.ElapsedMilliseconds}ms. Received: {result}");


            JObject resultJson;

            // Verify that result is valid JSON.
            try { 
                resultJson = JObject.Parse(result);
            }
            catch
            {
                resultJson = JObject.Parse($"{{\"error\": \"Invalid JSON returned from '{SensorDecoder}'\", \"rawpayload\": \"{base64Payload}\"}}");
            }

            return resultJson;

        }

        private static async Task<string> CallSensorDecoderModule(string sensorDecoderModuleUrl, byte[] payload)
        {
            var base64Payload = Convert.ToBase64String(payload);
            string result = "";

            try
            {
                HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
                client.DefaultRequestHeaders.Add("Keep-Alive", "timeout=86400");
                HttpResponseMessage response = await client.GetAsync(sensorDecoderModuleUrl);                

                if (!response.IsSuccessStatusCode)
                {
                    var badReqResult = await response.Content.ReadAsStringAsync();

                    result = JsonConvert.SerializeObject(new {
                            error = $"SensorDecoderModule '{sensorDecoderModuleUrl}' returned bad request.",
                            exceptionMessage = badReqResult ?? string.Empty,
                            rawpayload = base64Payload
                        });                     
                }
                else
                {
                    result = await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in decoder handling: {ex.ToString()}");
                
                result = JsonConvert.SerializeObject(new {
                    error = $"Call to SensorDecoderModule '{sensorDecoderModuleUrl}' failed.",
                    exceptionMessage = ex.ToString(),
                    rawpayload = base64Payload
                });
            }

            return result;
        }
    }
}
