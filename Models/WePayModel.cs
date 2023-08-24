using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WePayTest.Models
{
    public class WePayPaymentModel
    {
        public string Token { get; set; }

        public string PaymentMethodId { get; set; }
        public string CustomerName { get; set; }
        public string AppId { get; set; }
        public string UniqueKey { get; set; } = Guid.NewGuid().ToString();

        //amount in dollars
        public Int32 Amount { get; set;}

        public string AccountId { get; set; }

        public string EmailAddress { get; set; }

        public string Currency { get; set; }

        //Address.

        public string PostalCode { get; set; }

        public string Country { get; set; }
    }

    public class WePayMerchantOnBoardModel
    {
        public string Country { get; set; } 
        
        public string ControllerEmail { get; set; }

        public string AccountName { get; set; }

        public string AccountDescription { get; set; }

        public string MerchantCategoryCode { get; set; }

        public string MerchantIP { get; set; }

        public bool TOSAccepted { get; set; }
    }

    public class WePayReturnStatus
    {
        public bool IsSuccess { get; set; }
        public string StatusMsg { get; set; }

        public string WePayErrorCode { get; set; }

        public string WePayReasonCode { get; set; }

    }

}