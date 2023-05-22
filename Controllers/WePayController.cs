using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.UI.WebControls;
using System.Web.WebSockets;
using WePayTest.Models;

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
           

            data.PaymentMethodId = "00000000-6363-0000-0000-0011f2da12db";
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