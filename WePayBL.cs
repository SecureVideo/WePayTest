using Newtonsoft.Json.Linq;
using System;
using System.Collections;
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
using System.Web.WebSockets;
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
        
        private static readonly string[] m_notfications= new string[] {"accounts.capabilities.updated", "disputes.created",
                                                                       "disputes.resolved", "legal_entities.verifications.updated",
                                                                        "payments.canceled", "payments.failed","payouts.failed","refunds.created"}; 
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
            //is this the right way?
            //HostingEnvironment.QueueBackgroundWorkItem((token) => SubscribeToNotifications());
            //bool result = SubscribeToNotifications().Result;
            _ = SubscribeToNotifications();
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
                AddAccountLevelRbitInfo(data);
                msg.Content = new StringContent(data.ToString(), Encoding.UTF8, "application/json");

                response = await m_wePayHttpClient.SendAsync(msg);
                if (!response.IsSuccessStatusCode)
                {
                    string responseMsg = await response.Content.ReadAsStringAsync();
                    status.StatusMsg = GetErrorMessage(responseMsg);
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

        public static async Task<WePayReturnStatus> DeleteAccount(string accountId)
        {
            var status = new WePayReturnStatus() { IsSuccess = false };
            if (String.IsNullOrWhiteSpace(accountId))
            {
                return new WePayReturnStatus() { StatusMsg = "Empty account Id sent for delete" };
            }
            try
            {
                var request = GetDefaultRequestMessageWithHeaders(HttpMethod.Delete, $"/accounts/{accountId}");
                var response = await m_wePayHttpClient.SendAsync(request);
                status.IsSuccess = response.IsSuccessStatusCode;
                status.StatusMsg = GetErrorMessage(await response.Content.ReadAsStringAsync());
            }
            catch (Exception exc)
            {
                status.StatusMsg = exc.Message;
            }
            return status;
        }

        public static async  Task<WePayReturnStatus> DeletePaymentMethod(string paymentId)
        {
            var status = new WePayReturnStatus() { IsSuccess = false };
            if (String.IsNullOrWhiteSpace(paymentId))
            {
                return new WePayReturnStatus() { StatusMsg = "Empty payment method sent for delete" };
            }
            try
            {
                var request = GetDefaultRequestMessageWithHeaders(HttpMethod.Delete, $"/payment_methods/{paymentId}");
                var response = await m_wePayHttpClient.SendAsync(request);
                status.IsSuccess = response.IsSuccessStatusCode;
                status.StatusMsg = GetErrorMessage(await response.Content.ReadAsStringAsync());
            }
            catch(Exception exc)
            {
                status.StatusMsg = exc.Message;
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
                        rbits: [],
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
            //await SubscribeToNotifications();


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
                AddPaymentLevelRbitInfo(content);
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
                    HostingEnvironment.QueueBackgroundWorkItem((cancellationToken) => RetryRequest(payment.UniqueKey, content));
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
                initiated_by: 'customer',
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
                content["payment_method"]["credit_card"]["card_holder"]["holder_name"] = payment.CustomerName;
                content["payment_method"]["credit_card"]["card_holder"]["email"] = payment.EmailAddress;
                content["payment_method"]["credit_card"]["card_holder"]["address"]["country"] = payment.Country;
                content["payment_method"]["credit_card"]["card_holder"]["address"]["postal_code"] = payment.PostalCode;
                content["fee_amount"] = CalculateFees(payment.Amount * 100);
                content["payment_method"]["token"]["id"] = payment.Token;
                AddPaymentLevelRbitInfo(content);
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
                    HostingEnvironment.QueueBackgroundWorkItem((cancellationToken) => RetryRequest(payment.UniqueKey, content));
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

        private static void AddPaymentLevelRbitInfo(JObject data)
        {
            long receiveTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            //may want to check on this.
            string source = "partner_database";
            JArray rbitArray = new JArray();
            //add phone
            JObject phone = new JObject(new JProperty("receive_time", receiveTime),
                                    new JProperty("type", "phone"),
                                    new JProperty("source", source));
            phone["phone"] = new JObject(new JProperty("phone_number", "7192223456"), new JProperty("country_code", "1"));
            rbitArray.Add(phone);

            //add address
            JObject address = new JObject(new JProperty("receive_time", receiveTime),
                                    new JProperty("type", "address"),
                                    new JProperty("source", source));
            //origin_address is a nested object
            address["address"] = new JObject(new JProperty("origin_address", new JObject(new JProperty("postal_code", "76789"))));
            rbitArray.Add(address);

            //add itemized receipt
            JObject transactionDetails = new JObject(new JProperty("receive_time", receiveTime),
                                    new JProperty("type", "transaction_details"),
                                    new JProperty("source", source));
            JObject itemizedReceipt = new JObject(new JProperty("item_price", 50),
                                                    new JProperty("quantity", 1),
                                                    new JProperty("description", "Telehealth service"),
                                                    new JProperty("amount", 50));
            //itemized_receipt is a nested object that contains an array of objects
            transactionDetails["transaction_details"] = new JObject(new JProperty("itemized_receipt", new JArray(itemizedReceipt)));
            rbitArray.Add(transactionDetails);
            
            data["rbits"] = rbitArray;

        }

        private static void AddAccountLevelRbitInfo(JObject data)
        {
            long receiveTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string source = "partner_database";
            JArray rbitArray = new JArray();

            //add partner_Service
            JObject partnerService = new JObject(new JProperty("receive_time", receiveTime),
                                                new JProperty("type", "partner_service"),
                                                new JProperty("source", source));
            partnerService["partner_service"] = new JObject(new JProperty("service_monthly_cost", 50),
                                                             new JProperty("service_name", "telehealth"));
            rbitArray.Add(partnerService);

            //add external account
            JObject externalAccount = new JObject(new JProperty("receive_time", receiveTime),
                                                new JProperty("type", "external_account"),
                                                new JProperty("source", source));
            //question is_partner_account (should be true?) If its false, then account_type must be added(Name of the provider of the account.?)
            //not sure on this.
            externalAccount["external_account"] = new JObject(new JProperty("create_time", DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeSeconds()), 
                                                  new JProperty("is_partner_account", true),
                                                  new JProperty("account_type", ""));
            rbitArray.Add(externalAccount);         
            data["rbits"] = rbitArray;
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
        private static async Task RetryRequest(string uniqueKey, JObject data)
        {
            HttpStatusCode statusCode  = HttpStatusCode.InternalServerError;
            int i = 0;
            HttpResponseMessage response = null;
            //todo: ideally the requests should be retried for 24 hours from the initial request. Not sure if that is too much load on the server
            //will have to consider it.
            while (statusCode != HttpStatusCode.OK && i++ < m_retryIntervals.Length)
            {
                Thread.Sleep(m_retryIntervals[i] * 1000);

                response  = await m_wePayHttpClient.SendAsync(ClonePaymentRequestMessage(uniqueKey, data));
                statusCode = response.StatusCode;
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
            if (statusCode != HttpStatusCode.OK && i == m_retryIntervals.Length)
            {
                //log retries failed
                string correlatonID = response.Headers.GetValues("X-Correlation-Id").FirstOrDefault();
                //log this.
            }
           
        }

        private static async Task<bool> SubscribeToNotifications()
        {
            try
            {

                var request = GetDefaultRequestMessageWithHeaders(HttpMethod.Get, "/notification_preferences?page_size=10&status=active");
                var response = m_wePayHttpClient.SendAsync(request).Result;
                if (!response.IsSuccessStatusCode)
                {
                    string msg = GetErrorMessage(await response.Content.ReadAsStringAsync());
                    return false;
                }
                var data = JObject.Parse(await response.Content.ReadAsStringAsync());
                
                foreach(string notification in m_notfications)
                {
                    if (data["results"] != null  && 
                        data["results"].Any(result => result["topic"].ToString() == notification))
                    {
                        continue;
                    }
                    //register notifications
                    request = GetDefaultRequestMessageWithHeaders(HttpMethod.Post, "/notification_preferences");
                    JObject json = new JObject(new JProperty("callback_uri", "https://eoch9m2jwdh4pt5.m.pipedream.net"), 
                                                new JProperty("topic", notification));
                    request.Content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");

                    response = m_wePayHttpClient.SendAsync(request).Result;
                    if (!response.IsSuccessStatusCode)
                    {
                        string msg = GetErrorMessage(await response.Content.ReadAsStringAsync());
                        return false;
                    }
                }
            }
            catch(Exception exc)
            {
                //log
            }
            return true;
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

        private static HttpRequestMessage ClonePaymentRequestMessage(string uniqueKey, JObject data)
        {
            HttpRequestMessage newMessage = GetDefaultRequestMessageWithHeaders(HttpMethod.Post, "/payments");
            if (!string.IsNullOrWhiteSpace(uniqueKey))
                newMessage.Headers.Add("Unique-Key", uniqueKey);
            newMessage.Content = new StringContent(data.ToString(), Encoding.UTF8, "application/json");
            return newMessage;

        }



    }
}