using FikaAmazonAPI.Search;
using System;
using System.Collections.Generic;
using static FikaAmazonAPI.Utils.Constants;
using FikaAmazonAPI.AmazonSpApiSDK.Models.FulfillmentInbound;

namespace FikaAmazonAPI.Parameter.FulFillmentInbound
{
    public class ParameterGetShipments : ParameterBased
    {
        public ICollection<ShipmentStatus> ShipmentStatusList { get; set; }
        public ICollection<string> ShipmentIdList { get; set; }
        public DateTime? LastUpdatedAfter { get; set; }
        public DateTime? LastUpdatedBefore { get; set; }
        public QueryType QueryType { get; set; }
        public string NextToken { get; set; }
        public string MarketplaceId { get; set; }

        /// <summary>
        /// Pages to read (1 = only the first page, its <c>NextToken</c> left on the result). Null = every page. Not sent to Amazon.
        /// </summary>
        [IgnoreToAddParameter]
        public int? MaxNumberOfPages { get; set; }
    }
}
