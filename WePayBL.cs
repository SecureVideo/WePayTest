using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Remoting.Messaging;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Hosting;
using WePayTest.Models;

namespace WePayTest
{
    public class WePayBL
    {
        private static readonly HttpClient m_wePayHttpClient = null;
        private static readonly string m_stageUrl = "https://stage-api.wepay.com/";
        private static readonly string m_prodUrl = "https://api.wepay.com/";
        private static readonly string m_apiToken = string.Empty;
        private static readonly string m_appID = string.Empty;
        private static readonly string m_version = string.Empty;
        private static readonly int[] m_retryIntervals = new Int32[] {5,30,75,240,720,1800, 1800, 1800, 1800, 1800};
        static WePayBL()
        {
            m_wePayHttpClient = new HttpClient();
            m_wePayHttpClient.BaseAddress = new Uri(m_stageUrl);
            m_apiToken = "stage_MTk0NjBfNmIwY2U5M2UtMDg0Zi00ODQxLWE3MDctYzFjNDNjMTkyMmRm";
            m_appID = "332832";
            m_version = "3.0";
            //based on this article http://byterot.blogspot.com/2016/07/singleton-httpclient-dns.html and
            //https://github.com/dotnet/aspnetcore/issues/28385#issuecomment-853766480
            ServicePointManager.FindServicePoint(m_wePayHttpClient.BaseAddress).ConnectionLeaseTimeout = 60000;
        }

        public static async Task<WePayReturnStatus> OnBoardMerchant(WePayMerchantOnBoardModel merchantData)
        {
            WePayReturnStatus status = new WePayReturnStatus() { IsSuccess = false };
            try
            {
                //if (!merchantData.TOSAccepted)
                //{
                //    throw new Exception("Terms of Service  not accepted. Please accept Terms of Service and try again.");
                //}
                HttpRequestMessage msg = GetDefaultRequestMessageWithHeaders(HttpMethod.Post, "/legal_entities");
                JObject data = JObject.Parse(@"{
                        country: 'US',
                          terms_of_service: {
                            acceptance_time: null,
                            original_ip: null
                            },
                        controller: {
                            email: 'test@test.com'
                        }
                    }");

                data["country"] = merchantData.Country;
                data["controller"]["email"] = merchantData.ControllerEmail;
                //data["terms_of_Service"]["acceptantce_time"] = DateTime.UtcNow;
                msg.Content = new StringContent(data.ToString(), Encoding.UTF8, "application/json");
                HttpResponseMessage response =  await m_wePayHttpClient.SendAsync(msg);
                if (!response.IsSuccessStatusCode)
                {
                    status.StatusMsg = GetErrorMessage(await response.Content.ReadAsStringAsync());
                    //log StatusMsg.
                    return status;
                }
                //store this in DB?
                string legalEntityID = JObject.Parse(await response.Content.ReadAsStringAsync())["id"]?.ToString();
                
                //create account
                msg = GetDefaultRequestMessageWithHeaders(HttpMethod.Post, "/accounts");
                data = JObject.Parse(@"{
                        legal_entity_id: 'id',
                        name: 'name of account',
                        description: 'acct description',
                        industry: {
                            merchant_category_code: null
                            }
                        }");
                data["legal_entity_id"] = legalEntityID;
                data["name"] = merchantData.AccountName;
                data["description"] = merchantData.AccountDescription;
                data["industry"]["merchant_category_code"] = merchantData.MerchantCategoryCode;
                msg.Content = new StringContent(data.ToString(), Encoding.UTF8, "application/json");

                response = await m_wePayHttpClient.SendAsync(msg);
                if (!response.IsSuccessStatusCode)
                {
                    status.StatusMsg = GetErrorMessage(await response.Content.ReadAsStringAsync());
                    //log StatusMsg.
                    return status;
                }
                //log stage

                //store this in DB
                string accountID = JObject.Parse(await response.Content.ReadAsStringAsync())["id"]?.ToString();

                //send verification email
                msg = GetDefaultRequestMessageWithHeaders(HttpMethod.Post, $"/legal_entities/{legalEntityID}/set_controller_password");
                response = await m_wePayHttpClient.SendAsync(msg);
                status.IsSuccess = response.IsSuccessStatusCode;
                status.StatusMsg = response.IsSuccessStatusCode ? $"Verification email has been sent to {merchantData.ControllerEmail}. Please complete you KYC and other forms to activate the payment method."
                                                                :GetErrorMessage(await response.Content.ReadAsStringAsync());

                //log response

            }
            catch (Exception exc)
            {
                status.StatusMsg = exc.Message;
            }

            return status;
        }

        public static async Task<WePayReturnStatus> GetOnBoardingStatus(string entityID, string accountId)
        {
            WePayReturnStatus status = new WePayReturnStatus() { IsSuccess = false};

            try
            {
                HttpRequestMessage msg = GetDefaultRequestMessageWithHeaders(HttpMethod.Get, $"/legal_entities/{entityID}/verifications");
                HttpResponseMessage response = await m_wePayHttpClient.SendAsync(msg);
                string respMsg = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    status.StatusMsg = $"Error retrieving verifications status for entity id {entityID}. The error received is{GetErrorMessage(respMsg)}";
                    return status;
                }
               
                JObject data = JObject.Parse(respMsg);
                bool.TryParse(data["controller"]["personal_verification"]["verified"].ToString(), out bool verified);
                if (!verified)
                {
                    status.StatusMsg += "Controller has not been verified \r\n";
                }
                bool.TryParse(data["entity_verification"]["verified"].ToString(), out verified);

                if (!verified)
                {
                    status.StatusMsg += "Entity has not been verified \r\n";
                }

                //get status of account 
                msg = GetDefaultRequestMessageWithHeaders(HttpMethod.Get, $"/accounts/{accountId}/capabilities");
                response = await m_wePayHttpClient.SendAsync(msg);
                respMsg = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    status.StatusMsg = $"Error retrieving account capabilties for  account id {accountId}. The error received is {GetErrorMessage(respMsg)}";
                    return status;
                }

                data = JObject.Parse(respMsg);

                bool.TryParse(data["payments"]["enabled"].ToString(), out bool enabled);
                if (!enabled)
                {
                    status.StatusMsg += "Payments is not enabled for account \r\n";
                }

                bool.TryParse(data["payouts"]["enabled"].ToString(), out enabled);
                if (!enabled)
                {
                    status.StatusMsg += "Payouts is not enabled for account \r\n";
                }

                status.IsSuccess = string.IsNullOrWhiteSpace(status.StatusMsg);
            }
            catch (Exception exc)
            {
                status.StatusMsg = exc.Message;
            }

            return status;

        }

        public static async Task<WePayReturnStatus> GetCConFile(WePayPaymentModel data)
        {
            WePayReturnStatus status = new WePayReturnStatus() { IsSuccess = false };

            try
            {
                HttpRequestMessage msg = GetDefaultRequestMessageWithHeaders(HttpMethod.Get, $"/payment_methods/{data.PaymentMethodId}");
                msg.Headers.Add("Unique-Key", data.UniqueKey);
                HttpResponseMessage response =  await m_wePayHttpClient.SendAsync(msg);

                status.IsSuccess = response.IsSuccessStatusCode;
                status.StatusMsg = status.IsSuccess ? await response.Content.ReadAsStringAsync() : GetErrorMessage(await response.Content.ReadAsStringAsync());
            }
            catch (Exception exc)
            {
                status.StatusMsg= exc.Message;

            }

            return status;
        }
        public static async Task<WePayReturnStatus> SaveCreditCard(WePayPaymentModel payment)
        {
            WePayReturnStatus status = new WePayReturnStatus() { IsSuccess = false };

            try
            {
                //more validations.
                if (String.IsNullOrWhiteSpace(payment?.Token))
                {
                    status.StatusMsg = "Payment token not set";
                    return status;
                }

                HttpRequestMessage msg = GetDefaultRequestMessageWithHeaders(HttpMethod.Post, $"/payment_methods");
                msg.Headers.Add("Unique-Key", payment.UniqueKey);
                JObject data = JObject.Parse(@"{
                        type: 'credit_card',
                        token: {
                            id: ''                      
                            },
                        credit_card: 
                        {
                            card_on_file : true,
                            card_holder: {
                                holder_name: 'Test Test',
                                email: 'testest@hotmail.com',
                                address: {
                                    country: 'US',
                                    postal_code: '94025'
                                     }
                                }                            
                           }
                        }");
             
                data["token"]["id"] = payment.Token;
                data["credit_card"]["card_holder"]["holder_name"] = payment.CustomerName;
                data["credit_card"]["card_holder"]["email"] = payment.EmailAddress;
                data["credit_card"]["card_holder"]["address"]["country"] = payment.Country;
                data["credit_card"]["card_holder"]["address"]["postal_code"] = payment.PostalCode;
                msg.Content = new StringContent(data.ToString(), Encoding.UTF8,"application/json");
                HttpResponseMessage response = await m_wePayHttpClient.SendAsync(msg);
                string respMsg = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    status.StatusMsg = $"Error creating payment method for credit card. The response retrieved is {GetErrorMessage(respMsg)}";
                }
                else
                {
                    //maybe not send the id?
                    string paymentId = JObject.Parse(respMsg)["id"].ToString();
                    status.StatusMsg = paymentId ;
                    status.IsSuccess = true;
                    //store the ID
                }
                

            }
            catch (Exception exc)
            {
                status.StatusMsg = exc.Message;
            }
            return status;
        }

        public static async Task<WePayReturnStatus> MakePaymentUsingPaymentMethod(WePayPaymentModel payment)
        {

            WePayReturnStatus status = new WePayReturnStatus() { IsSuccess = false };
            try
            {
                if (String.IsNullOrWhiteSpace(payment?.PaymentMethodId))
                {
                    status.StatusMsg = "Payment method Id not set";
                    return status;
                }

                HttpRequestMessage msg = GetDefaultRequestMessageWithHeaders(HttpMethod.Post, "/payments");
                msg.Headers.Add("Unique-Key", payment.UniqueKey);
                JObject content = JObject.Parse(@"{
                account_id:'',
                amount: 0,
                auto_capture: true,
                currency: 'USD',
                fee_amount: 0,
                initiated_by: 'customer',
                payment_method: {
                   type: 'payment_method_id',
                   payment_method_id: ''
                    }
                }");
                content["account_id"] = payment.AccountId;
                content["amount"] = payment.Amount * 100;
                content["currency"] = payment.Currency;
                //content["credit_card"]["card_holder"]["holder_name"] = payment.CustomerName;
                
                content["fee_amount"] = CalculateFees(payment.Amount * 100);
                content["payment_method"]["payment_method_id"] = payment.PaymentMethodId;

                msg.Content = new StringContent(content.ToString(), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await m_wePayHttpClient.SendAsync(msg);
                string responseContent = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    status.IsSuccess = true;
                }
                else if (IsRetryRequired(responseContent, response.StatusCode))
                {
                    //launch retry
                    HostingEnvironment.QueueBackgroundWorkItem((cancellationToken) => RetryRequest(msg));
                }
                else
                {
                    status.StatusMsg = GetErrorMessage(responseContent);
                }

            }
            catch (Exception exc)
            {
                status.StatusMsg = exc.Message;
            }

            return status;
        }
        public static async Task<WePayReturnStatus> MakePayment(WePayPaymentModel payment)
        {
           
            WePayReturnStatus status = new WePayReturnStatus() { IsSuccess = false}; 
            try
            {
                HttpRequestMessage msg = GetDefaultRequestMessageWithHeaders(HttpMethod.Post, "/payments");
                msg.Headers.Add("Unique-Key", payment.UniqueKey);
                JObject content = JObject.Parse(@"{
                account_id:'',
                amount: 0,
                auto_capture: true,
                currency: 'USD',
                fee_amount: 0,
                payment_method: {
                    token: {
                        id: 'test'
                    },
                    credit_card: {
                        card_holder: {
                            holder_name: 'Test Test',
                            email: 'testest@hotmail.com',
                            address: {
                                country: 'US',
                                postal_code: '94025'
                            }
                        }
                    }
                }
                }");
                content["account_id"] = payment.AccountId;
                content["amount"] = payment.Amount * 100;
                content["currency"] = payment.Currency;
                content["credit_card"]["card_holder"]["holder_name"] = payment.CustomerName;
                content["credit_card"]["card_holder"]["email"] = payment.EmailAddress;
                content["credit_card"]["card_holder"]["address"]["country"] = payment.Country;
                content["credit_card"]["card_holder"]["address"]["postal_code"] = payment.PostalCode;
                content["fee_amount"] = CalculateFees(payment.Amount * 100);
                content["token"]["id"] = payment.Token;
               
                msg.Content = new StringContent(content.ToString(),Encoding.UTF8,"application/json");

                HttpResponseMessage response = await m_wePayHttpClient.SendAsync(msg);
                string responseContent= await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    status.IsSuccess = true;
                }
                else if (IsRetryRequired(responseContent, response.StatusCode))
                {
                    //launch retry
                    HostingEnvironment.QueueBackgroundWorkItem((cancellationToken) => RetryRequest(msg));
                }
                else
                {
                    status.StatusMsg = GetErrorMessage(responseContent);
                }

                //log statusMsg and status

            }
            catch(Exception exc)
            {
                status.StatusMsg = exc.Message;
            }

            return status;
        }
         
        private static bool IsRetryRequired(string errorResponse, HttpStatusCode statusCode)
        {
            if (statusCode == HttpStatusCode.InternalServerError || statusCode ==  HttpStatusCode.Conflict )
                return true;
            
            if (JObject.Parse(errorResponse)["details"] == null)
                throw new Exception($"Invalid error response from wepay. {errorResponse}");

            if (JObject.Parse(errorResponse)["details"].Any(a => a["reason_code"].ToString() == "CONCURRENT_UNIQUE_KEY_REQUEST_IS_PROCESSING"  || 
                                                                 a["reason_code"].ToString() == "APPLICATION_REQUEST_THROTTLE_EXCEEDED"))
                return true;

            return false;
        }

        private static string GetErrorMessage(string errorResponse) => JObject.Parse(errorResponse)["error_message"]?.ToString();

        //maybe move code to a worker? Need to find ideal way to spawn this.
        private static void RetryRequest(HttpRequestMessage requestMessage)
        {
            HttpStatusCode statusCode  = HttpStatusCode.InternalServerError;
            int i = 0;
            while (statusCode != HttpStatusCode.OK && i++ < m_retryIntervals.Length)
            {
                Thread.Sleep(m_retryIntervals[i] * 1000);

                HttpResponseMessage response  = m_wePayHttpClient.SendAsync(CloneMessage(requestMessage).Result).Result;
                if (response.IsSuccessStatusCode)
                {
                    //log success
                    break;
                }
                else
                {
                    string responseContent = response.Content.ReadAsStringAsync().Result;
                }
            }
           
        }

        private static int CalculateFees(int amount)
        {
            return Convert.ToInt32((0.029 * amount) + 30);//2.9% +0.30 cents
        }

    
        private static  HttpRequestMessage GetDefaultRequestMessageWithHeaders(HttpMethod method, string url)
        {
            HttpRequestMessage requestMessage = new HttpRequestMessage(method, url);
            requestMessage.Headers.Add("Api-Version", m_version);
            requestMessage.Headers.Add("App-Id", m_appID);
            requestMessage.Headers.Add("App-Token", m_apiToken);
            return requestMessage;

        }

        private static async Task<HttpRequestMessage> CloneMessage(HttpRequestMessage msg)
        {
            HttpRequestMessage newMessage = GetDefaultRequestMessageWithHeaders(msg.Method, msg.RequestUri.AbsolutePath);
            if (msg.Headers.Contains("Unique-Key"))
                newMessage.Headers.Add("Unique-Key", msg.Headers.GetValues("Unique-Key").First());
            var ms = new MemoryStream();
            if (msg.Content != null)
            {
                await msg.Content.CopyToAsync(ms);
                ms.Position = 0;
                newMessage.Content = new StreamContent(ms);
            }
            return newMessage;

        }



    }
}