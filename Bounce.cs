using System.Collections.Generic;

namespace AWSEmailWebService
{
    internal class Bounce
    {
        public string bounceType { get; set; }
        public string bounceSubType { get; set; }
        public IList<Recipient> bouncedRecipients { get; set; }
        public string timestamp { get; set; }
        public string feedbackId { get; set; }
        public string reportingMTA { get; set; }
        public string remoteMtaIp { get; set; }
    }
}