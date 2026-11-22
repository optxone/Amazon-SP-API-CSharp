using FikaAmazonAPI.AmazonSpApiSDK.Models.FulfillmentInbound;
using FikaAmazonAPI.Services;

namespace Tests
{
    /// <summary>getShipments used to return only its first page (NextToken ignored).</summary>
    public class GetShipmentsPagingTests
    {
        private static GetShipmentsResult Page(string next, params string[] ids)
        {
            var list = new InboundShipmentList();
            list.AddRange(ids.Select(id => new InboundShipmentInfo { ShipmentId = id }));
            return new GetShipmentsResult { ShipmentData = list, NextToken = next };
        }

        [Test]
        public async Task ReadsEveryPage_AndClearsTheToken()
        {
            var pages = new Dictionary<string, GetShipmentsResult> { ["t1"] = Page("t2", "C"), ["t2"] = Page(null, "D") };
            var asked = new List<string>();

            var result = await FulFillmentInboundService.ReadRemainingShipmentPagesAsync(Page("t1", "A", "B"), null,
                token => { asked.Add(token); return Task.FromResult(pages[token]); });

            Assert.That(result.ShipmentData.Select(s => s.ShipmentId), Is.EqualTo(new[] { "A", "B", "C", "D" }));
            Assert.That(asked, Is.EqualTo(new[] { "t1", "t2" }));
            Assert.That(result.NextToken, Is.Null);
        }

        [Test]
        public async Task OnePage_KeepsTheToken()
        {
            var result = await FulFillmentInboundService.ReadRemainingShipmentPagesAsync(Page("t1", "A"), 1,
                _ => throw new InvalidOperationException("no second page expected"));

            Assert.That(result.ShipmentData, Has.Count.EqualTo(1));
            Assert.That(result.NextToken, Is.EqualTo("t1"));
        }

        [Test]
        public async Task MaxPages_StopsAndLeavesTheNextToken()
        {
            var pages = new Dictionary<string, GetShipmentsResult> { ["t1"] = Page("t2", "B"), ["t2"] = Page("t3", "C") };

            var result = await FulFillmentInboundService.ReadRemainingShipmentPagesAsync(Page("t1", "A"), 2,
                token => Task.FromResult(pages[token]));

            Assert.That(result.ShipmentData.Select(s => s.ShipmentId), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(result.NextToken, Is.EqualTo("t2"));
        }
    }
}
