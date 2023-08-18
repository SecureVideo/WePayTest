using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.UI.WebControls;
using System.Web.WebSockets;
using WePayTest.Models;
using ActionResult = System.Web.Mvc.ActionResult;

namespace WePayTest.Controllers
{
    public class WePayController : Controller
    {
        // GET: WePay

        

   
        public ActionResult Index()
        {
            return View(new WePayTest.Models.WePayPaymentModel() { AppId= "332832" });
        }

        async public Task<ActionResult> GetMerchantOnBoardingStatus()
        {
            WePayReturnStatus returnStatus = await WePayBL.GetOnBoardingStatus("06deec25-9c30-43fd-af16-fe508d84ba4f", "1f251d35-61ec-4389-abe3-0ae085857bc2");

            return Content(returnStatus.StatusMsg);
        }
        async public Task<ActionResult> OnBoardMerchant()
        {
            WePayMerchantOnBoardModel model = new WePayMerchantOnBoardModel()
            {
                AccountName = "Live Celly Account",
                AccountDescription = "From the controoler",
                ControllerEmail = "cellysumit45@hotmail.com",
                Country = "US",
                MerchantCategoryCode = "8011",
            };

            WePayReturnStatus returnStatus = await WePayBL.OnBoardMerchant(model);

            return Content(returnStatus.StatusMsg);
        }

        public async Task<ActionResult> DeleteSavedPaymentMethod(string id)
        {
            WePayReturnStatus returnStatus = await WePayBL.DeletePaymentMethod("00000000-6363-0000-0000-0048f2e2a88c");

            return Content(returnStatus.StatusMsg);
        }

        public async Task<ActionResult> SaveCC(WePayPaymentModel data)
        {
            data.CustomerName = "test guy1";
            data.EmailAddress = "sumitcelly@yahoo.com";
            data.Amount = 25;
            data.Currency = "USD";
            data.Country = "US";
            data.PostalCode = "88888";
            var status = await WePayBL.SaveCreditCard(data);

            return Content(status.StatusMsg);
        }

        public async Task<ActionResult> GetCConFile()
        {
            WePayPaymentModel model = new WePayPaymentModel() { PaymentMethodId = "00000000-6363-0000-0000-003411088f14" };
            WePayReturnStatus returnStatus = await WePayBL.GetCConFile(model);
            return Content(returnStatus.StatusMsg);
        }

        public async Task<ActionResult> MakePaymentUsingPaymentMethod(WePayPaymentModel data)
        {
            //look at unique key and retry for payments for errors in the BL
            //the account id below is linked to sumitcelly@hotmail.com
            //data.AccountId = "98a70dc3-5a6b-4483-a3b7-8527c4c070f3";

            //the acct id here is linked to cellysumit@hotmail.com
            data.AccountId = "1f251d35-61ec-4389-abe3-0ae085857bc2";

          
            data.Amount = 25;
            data.Currency = "USD";
            data.Country = "US";
           

            data.PaymentMethodId = "00000000-6363-0000-0000-003411088f14";
            var status = await WePayBL.MakePaymentUsingPaymentMethod(data);

            return Content(status.StatusMsg);
        }
        public async Task<ActionResult> MakePayment(WePayPaymentModel data)
        {
            //look at unique key and retry for payments for errors in the BL
            //the account id below is linked to sumitcelly@hotmail.com
            //data.AccountId = "98a70dc3-5a6b-4483-a3b7-8527c4c070f3";
            
            //the acct id here is linked to cellysumit@hotmail.com
            data.AccountId = "1f251d35-61ec-4389-abe3-0ae085857bc2";

            data.CustomerName = "test guy1";
            data.EmailAddress = "sumitcelly@yahoo.com";
            data.Amount = 25;
            data.Currency = "USD";
            data.Country = "US";
            data.PostalCode = "88888";

            var status = await WePayBL.MakePayment(data);
           
            return Content(status.StatusMsg);
           }

        [AllowAnonymous]
        public  Microsoft.AspNetCore.Mvc.ActionResult Update(ZoomWebhookPayload zoomData)
        { 
        //{
        //    string encryptedToken = EncryptionUtils.ToHex(EncryptionUtils.HashHMAC("rT8Te39iQFeUEw_tvgbaaA", zoomData.payload.plainToken));
        //    //return Ok(
        //    //    new ZoomReturnResult() { encryptedToken = encryptedToken, plainToken = zoomData.payload.plainToken }
        //    //); ;
        //    var retData = new { encryptedToken = encryptedToken, plainToken = zoomData.payload.plainToken };
        //    return new OkObjectResult(new { encryptedToken = encryptedToken, plainToken = zoomData.payload.plainToken });

             string hashed = "";
        string requestBody = String.Empty;
       // 

            using (Stream req = Request.InputStream)
            {
                req.Seek(0, System.IO.SeekOrigin.Begin);
                requestBody = new StreamReader(req).ReadToEnd();
            }
            var data = JsonConvert.DeserializeObject<ZoomWebhookPayload>(requestBody);
            if (!(string.IsNullOrEmpty(data.Event)) && data.Event == "endpoint.url_validation")
            {
                var encoding = new System.Text.ASCIIEncoding();
                var sha256 = new System.Security.Cryptography.HMACSHA256();
                sha256.Key = encoding.GetBytes("rT8Te39iQFeUEw_tvgbaaA");
                var hash = sha256.ComputeHash(encoding.GetBytes(data.payload.plainToken));
                hashed = ToHex(hash, false);
            }
            return new OkObjectResult(new
            {
                plainToken = data.payload.plainToken,
                encryptedToken = hashed
             });
        }

        private static string ToHex(byte[] bytes, bool upperCase)
        {
            StringBuilder result = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                result.Append(bytes[i].ToString(upperCase ? "X2" : "x2"));
            return result.ToString();
        }

        public class ZoomWebhookPayload
        {
            public ZoomWebhookEventPayload payload { get; set; }
            public string Event { get; set; }
        }

        public class ZoomWebhookEventPayload
        {
            public string plainToken { get; set; }

        }
        //public class ZoomWebhook
        //{
        //    public string @event { get; set; }
        //    public ZoomWebhookPayload payload { get; set; }
        //    public string download_token { get; set; }      // Used for recording_completed event

        //    public override string ToString()
        //    {
        //        return JsonConvert.SerializeObject(this);
        //    }
        //}
        //public class ZoomWebhookPayload
        //{
        //    public string account_id { get; set; }

        //    public string plainToken { get; set; }
            
        //}
        public static class EncryptionUtils
        {
            public static byte[] HashHMAC(string key, string message)
            {
                var hash = new HMACSHA256(Encoding.UTF8.GetBytes(key));
                return hash.ComputeHash(Encoding.UTF8.GetBytes(message));
                //return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(message))).Replace("-",String.Empty).ToLower();
            }

            public static string ToHex(byte[] bytes, bool upperCase = false)
            {
                StringBuilder result = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    result.Append(bytes[i].ToString(upperCase ? "X2" : "x2"));
                return result.ToString();
            }
        }
        private JObject GetContent(string token)
        {
            JObject data = JObject.Parse(@"{
                account_id: '98a70dc3-5a6b-4483-a3b7-8527c4c070f3',
                amount: 2000,
                auto_capture: true,
                currency: 'USD',
                fee_amount: 300,
                payment_method: {
                    token: {
                        id: 'test'
                    },
                    credit_card: {
                        card_holder: {
                            holder_name: 'Test Test',
                            email: 'cellysumit@hotmail.com',
                            address: {
                                country: 'US',
                                postal_code: '94025'
                            }
                        }
                    }
                }
             }");
            data["payment_method"]["token"]["id"] = token;
            return data;
        }
    }
}