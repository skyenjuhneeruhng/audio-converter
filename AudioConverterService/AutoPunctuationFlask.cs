using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace AudioConverterService
{
    // Web service input
    public class WsInput
    {
        public string transcript { get; set; }
    }

    public class AutoPunctuationFlask
    {
        public static string ScriptWithAutoPunc { get; private set; }
        public static string ApiErrorMessage { get; private set; }
        public static HttpResponseMessage Response { get; private set; }
        public static bool IsSuccessfully { get; private set; }

        /// <summary>
        /// Calling AutoPunctuation API to add punctuation to the input script.
        /// </summary>
        /// <param name="script"></param>
        /// <returns></returns>
        public static async Task InvokeRequestResponseService(String script)
        {
            string requestUrl =  ConfigurationManager.AppSettings["AutoPunctuationApiUrl"];  // Get url from App.config file.

            if (string.IsNullOrWhiteSpace(requestUrl))
            {
                throw new Exception("The AutoPunctuationApiUrl must be set in App.config.");
            }

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(requestUrl);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // WARNING: The 'await' statement below can result in a deadlock if you are calling this code from the UI thread of an ASP.Net application.
                // One way to address this would be to call ConfigureAwait(false) so that the execution does not attempt to resume on the original context.
                // For instance, replace code such as:
                //      result = await DoSomeTask()
                // with the following:
                //      result = await DoSomeTask().ConfigureAwait(false)

                WsInput wsinput = new WsInput { transcript = script };
                try
                {
                    Response = await client.PostAsJsonAsync(requestUrl, wsinput);
                }
                catch(Exception ex)
                {
                    IsSuccessfully = false;
                    ApiErrorMessage = ex.ToString();
                }

                //Response = await client.PostAsJsonAsync(requestUrl, wsinput);
                if(Response != null)   // If connected to the API server.
                {
                    if (Response.IsSuccessStatusCode)
                    {
                        IsSuccessfully = true;
                        string resultRaw = await Response.Content.ReadAsStringAsync();  //Raw string result with punctuation token.
                        ScriptWithAutoPunc = ReplaceToken(resultRaw);
                    }
                    else
                    {
                        IsSuccessfully = false;
                        StringBuilder sb = new StringBuilder();
                        sb.Append(string.Format("The request failed with status code: {0}", Response.StatusCode)).Append(Environment.NewLine);

                        // Append the headers - they include the requert ID and the timestamp, which are useful for debugging the failure
                        sb.Append(Response.Headers.ToString()).Append(Environment.NewLine);
                        string responseContent = await Response.Content.ReadAsStringAsync();
                        sb.Append(responseContent);
                        ApiErrorMessage = sb.ToString();
                    }
                }
                
            }
        }

        /// <summary>
        /// Replace the token from web service output with proper punctuation.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static string ReplaceToken(string input)
        {
            string[] tokens = { " ?QUESTIONMARK", " !EXCLAMATIONMARK", " ,COMMA", " -DASH", " :COLON", " ;SEMICOLON", " .PERIOD" };

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case " ?QUESTIONMARK":
                        input = input.Replace(" ?QUESTIONMARK", "?");
                        break;
                    case " !EXCLAMATIONMARK":
                        input = input.Replace(" !EXCLAMATIONMARK", "!");
                        break;
                    case " ,COMMA":
                        input = input.Replace(" ,COMMA", ",");
                        break;
                    case " -DASH":
                        input = input.Replace(" -DASH", "-");
                        break;
                    case " :COLON":
                        input = input.Replace(" :COLON", ":");
                        break;
                    case " ;SEMICOLON":
                        input = input.Replace(" ;SEMICOLON", ";");
                        break;
                    case " .PERIOD":
                        input = input.Replace(" .PERIOD", ".");
                        break;
                    default:
                        break;
                }
            }

            return input;
        }


    }
}
