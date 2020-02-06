using System.Collections.Generic;

namespace AWSEmailWebService
{
    internal class Complaint
    {
        public string userAgent { get; set; }
        public IList<Recipient> complainedRecipients { get; set; }
        public string timestamp { get; set; }
        public string feedbackId { get; set; }
        public string complaintFeedbackType { get; set; }
        public string arrivalDate { get; set; }
    }
}