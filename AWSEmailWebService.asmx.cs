using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Web.Configuration;
using System.Web.Script.Services;
using System.Web.Services;
using System.Web;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AWSEmailWebService
{
    /// <summary>
    /// Summary description for AWSEmailWebService
    /// </summary>
    [WebService(Namespace = "https://localhost/AWSEmailWebService")]
    [ScriptService]

    public class AWSEmailWebService : System.Web.Services.WebService
    {
        

        public AWSEmailWebService()
        {
                        
        }

        [WebMethod]
        public string SendTestEmail(string toAddress)
        {
            try
            {                
                var message = new MailMessage();
                message.To.Add(new MailAddress(toAddress, "Test Recipient"));
                message.From = new MailAddress("NoReply@gmail.com", "Test Subject");
                message.Subject = "test";
                message.Body = "This is a test";
                ThreadPool.QueueUserWorkItem(delegate { SendEmail(message, "AWSEmailWebService.asmx SendTestEmail"); });
                return "";
            }            
            catch (Exception ex)
            {
                return ex.Message + ex.StackTrace;
            }
        }

        [WebMethod]
        public string SendTestEmailWithAttachment(string toAddress)
        {
            try
            {
                var message = new MailMessage();
                message.To.Add(new MailAddress(toAddress, "Test Recipient"));
                message.From = new MailAddress("NoReply@gmail.com", "Test Subject");
                message.Subject = "test";
                message.Body = "This is a test";

                var attachment = new Attachment(@"C:\hi.txt");

                message.Attachments.Add(attachment);

                ThreadPool.QueueUserWorkItem(delegate { SendEmail(message, "AWSEmailWebService.asmx SendTestEmailWithAttachment"); });
                return "";
            }
            catch (Exception ex)
            {
                return ex.Message + ex.StackTrace;
            }
        }

        [WebMethod]
        public string SendMessage(string toName, string toAddress, string fromName, string fromAddress, string subject, string body, string callingAppName = "unknown")
        {
            //call .net email validation to make sure its a valid email address.
            bool isCell = false;

            try
            {
                toAddress = toAddress.Replace(" ", "");
                toAddress = toAddress.Replace(",,", "");
                toAddress = toAddress.Replace(";;", "");
                toAddress = Regex.Replace(toAddress.Trim(), "@[^\u0021-\u007E]+", string.Empty);
                toAddress = toAddress.TrimEnd(';', ',', ' ');

                // split "To" address and add each recipient, as necessary
                char[] Splitter = { ',', ';', ':' };

                string[] AddressCollection = toAddress.Length <= 0 ? new[] { "" } : toAddress.Split(Splitter);
                string[] NameCollection = toName.Length <= 0 ? new[] { "" } : toName.Split(Splitter);

                if (string.Join("", AddressCollection) == "")
                {
                    throw new Exception("AddressCollection blank. No email addresses specified.");
                }

                // include client link to top of email
                int dbPos = callingAppName.IndexOf("db");
                string dbSchema = "";
                var sBody = ""; // safe string

                if (dbPos >= 0)
                {
                    try
                    {
                        dbSchema = callingAppName.Substring(dbPos + 2);

                        var usBody = body; // "us" -- unsafe string

                        if (HttpUtility.UrlDecode(body).Length < body.Length) // already encoded
                        {
                            sBody = body;
                        }
                        else
                        {
                            sBody = HttpUtility.UrlEncode(body);
                        }

                    }
                    catch { }
                }

                // Testing shows upper limit of recipient list is 50. After that, emails will not be sent.
                //
                int recipientsPerGroup = Convert.ToInt32(WebConfigurationManager.AppSettings["RecipientsPerSendEmail"]);

                if (AddressCollection.Length < recipientsPerGroup)
                {
                    var emailAddresses = string.Join(",", AddressCollection.Where(a => !IsCell(a) && IsValidEmail(a)).ToArray());

                    var smsAddresses = string.Join(",", AddressCollection.Where(a => IsCell(a) && IsValidEmail(a)).ToArray());

                    if (smsAddresses != "")
                    {
                        var smsMessage = new MailMessage("no-reply@gmail.com", string.Join(",", smsAddresses));

                        smsMessage.From = new MailAddress("noreply@gmail.com", fromName, System.Text.Encoding.UTF8);
                        smsMessage.Sender = new MailAddress("noreply@gmail.com", fromName);
                        smsMessage.Subject = subject;

                        smsMessage.SubjectEncoding = System.Text.Encoding.UTF8;
                        smsMessage.BodyEncoding = System.Text.Encoding.UTF8;

                        if (smsMessage.To.Count > 0)
                        {
                            if (sBody != "")
                            {
                                body = HttpUtility.UrlDecode(sBody);
                            }

                            smsMessage.Body = body.Replace("<br>", "\r\n");
                            ThreadPool.QueueUserWorkItem(delegate { SendSMS(smsMessage, callingAppName); });
                        }
                    }

                    if (emailAddresses != "")
                    {
                        var mailMessage = new MailMessage("no-reply@gmail.com", string.Join(",", emailAddresses));

                        mailMessage.From = new MailAddress("noreply@gmail.com", fromName, System.Text.Encoding.UTF8);
                        mailMessage.Sender = new MailAddress("noreply@gmail.com", fromName);
                        mailMessage.Subject = subject;

                        mailMessage.SubjectEncoding = System.Text.Encoding.UTF8;
                        mailMessage.BodyEncoding = System.Text.Encoding.UTF8;

                        if (mailMessage.To.Count > 0)
                        {
                            if (dbSchema.Trim() != "")
                            {
                                body = $@"<div><a href=""https://gmail.com/{dbSchema}"">" +
                                        $@"<span style=""font-size:12px;"">Click here to go to <strong>gmail.com/{dbSchema.Replace("%2D", "-")}</strong></a></span></div><br><br>" +
                                        $@"<table><tr><td>" + HttpUtility.UrlDecode(sBody) + "</td></tr></table>";
                            }

                            mailMessage.Body = body.Replace("\r\n", "<br>");
                            mailMessage.Body = mailMessage.Body.Replace("\n", "<br>");
                            ThreadPool.QueueUserWorkItem(delegate { SendEmail(mailMessage, callingAppName); });
                        }
                    }
                }
                else // split into groups
                {
                    int maxDegreesOfParallelism = Convert.ToInt32(WebConfigurationManager.AppSettings["MaxDegreesOfParallelismPerSendMessage"]);
                    int numGroups = AddressCollection.Length / recipientsPerGroup + 1;
                    var p = Enumerable.Range(0, numGroups).ToArray();

                    Parallel.ForEach(p, new ParallelOptions { MaxDegreeOfParallelism = maxDegreesOfParallelism }, (i) =>
                    {
                        var addressesToSend = AddressCollection.Skip(i * recipientsPerGroup).Take(recipientsPerGroup).ToList();
                        var namesToUse = NameCollection.Skip(i * recipientsPerGroup).Take(recipientsPerGroup).ToList();

                        if (addressesToSend.Count == 0) return;

                        // initialize email message
                        var message = new MailMessage();

                        for (int x = 0; x < addressesToSend.Count; ++x)
                        {
                            var recipientEmail = "";
                            var recipientName = "";

                            if (!string.IsNullOrEmpty(addressesToSend.ElementAt(x).Trim()))
                            {
                                recipientEmail = Regex.Replace(addressesToSend[x].Trim(), "@[^\u0021-\u007E]+", string.Empty);

                                if (namesToUse.Count == addressesToSend.Count)
                                {
                                    if (namesToUse[x].Length > 160)
                                    {
                                        namesToUse[x] = namesToUse[x].Substring(0, 147) + "...";
                                    }

                                    recipientName = namesToUse[x].Trim();
                                }
                                else
                                {
                                    if (toName.Length > 160)
                                    {
                                        recipientName = toName.Substring(0, 147) + "...";
                                    }
                                    else
                                    {
                                        recipientName = toName;
                                    }
                                }

                                bool isError = false;
                                //call .net email validation to make sure its a valid email address.
                                try
                                {
                                    MailAddress mailAddress = new MailAddress(recipientEmail);
                                    string cellNum = mailAddress.User;
                                    if (IsNumber(cellNum) && (cellNum.Length == 10 || cellNum.Length == 11)) // +1 US international
                                    {
                                        isCell = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    WriteToDBLog(ex.Message + ex.StackTrace + " Invalid email address. recipientEmail: " + recipientEmail + " callingAppName = " + callingAppName);
                                    Context.Response.StatusCode = 500;
                                    isError = true;
                                }

                                if (isError) continue;

                                message.To.Add(new MailAddress(recipientEmail, recipientName, System.Text.Encoding.UTF8));
                            }
                        }

                        if (message.To.Count == 0)
                        {
                            throw new Exception("message.To.Count is zero.");
                        }

                        // initialize remaining message details
                        message.From = new MailAddress("noreply@gmail.com", fromName, System.Text.Encoding.UTF8);
                        message.Sender = new MailAddress("noreply@gmail.com", fromName);
                        message.Subject = subject;

                        message.SubjectEncoding = System.Text.Encoding.UTF8;
                        message.BodyEncoding = System.Text.Encoding.UTF8;

                        // call function to send email asynchronously                
                        if (isCell)
                        {
                            if (sBody != "")
                            {
                                body = HttpUtility.UrlDecode(sBody);
                            }

                            message.Body = body.Replace("<br>", "\r\n");
                            ThreadPool.QueueUserWorkItem(delegate { SendSMS(message, callingAppName); });
                        }
                        else
                        {
                            if (dbSchema.Trim() != "")
                            {
                                body = $@"<div><a href=""https://gmail.com/{dbSchema}"">" +
                                       $@"<span style=""font-size:12px;"">Click here to go to <strong>gmail.com/{dbSchema.Replace("%2D", "-")}</strong></a></span></div><br><br>" +
                                       $@"<table><tr><td>" + HttpUtility.UrlDecode(sBody) + "</td></tr></table>";
                            }

                            message.Body = body.Replace("\r\n", "<br>");
                            message.Body = message.Body.Replace("\n", "<br>");
                            ThreadPool.QueueUserWorkItem(delegate { SendEmail(message, callingAppName); });
                        }
                    });
                }

                return "";   // message send successful
            }            
            catch (Exception ex)
            {
                WriteToDBLog(ex.Message + ex.StackTrace + " toName = " + toName + " toAddress = " + toAddress + " fromName = " + fromName + " fromAddress = " + fromAddress +
                    " subject = " + subject + " body = " + body + " callingAppName = " + callingAppName);
                Context.Response.StatusCode = 500;
                return ex.Message + ex.StackTrace;
            }
        }


        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public void HandleBounce() //This method is called from Amazon Simple Notification Service when we receive a bounce.
        {
            string notification = "";
            using (var stream = new MemoryStream())
            {
                var request = HttpContext.Current.Request;
                request.InputStream.Seek(0, SeekOrigin.Begin);
                request.InputStream.CopyTo(stream);
                notification = Encoding.UTF8.GetString(stream.ToArray());
            }

            //First we need to handle the amazon notification.
            string json = HandleNotification(notification);
            if(json == "")
            {
                WriteToEventLog("Failed to get JSON event."); //This gets called when the system wasn't able to get the bounce or complaint json data.
                return;
            }
            if (json == "subscribed")
            {
                WriteToDBLog("Subscribed to the SNS system.");
                return;
            }

            Bounce incBounce;
            try
            {
                incBounce = JsonConvert.DeserializeObject<Bounce>(json); //A full list of bounce parameters can be found here: http://docs.aws.amazon.com/ses/latest/DeveloperGuide/notification-contents.html#bounce-object
            }
            catch (Exception ex)
            {
                WriteToEventLog(ex.Message + ex.StackTrace);
                return;
            }

            if (incBounce.bounceType == "Permanent") //remove all hard bounces from the database
            {
                foreach (Recipient rec in incBounce.bouncedRecipients)
                {
                    string emlAddr = rec.emailAddress ?? "";
                    if (emlAddr != "") { RemoveEmail(emlAddr); }
                }
                WriteToDBLog("Permanent bounce reported from Amazon SNS. Details of the bounced request: " + json);
            }
            else if (incBounce.bounceType == "Transient") //remove soft bounces if they are bounce issues that we can't fix
            {
                if (incBounce.bounceSubType == "ContentRejected" || incBounce.bounceSubType == "MessageTooLarge")
                {
                    foreach (Recipient rec in incBounce.bouncedRecipients)
                    {
                        string emlAddr = rec.emailAddress ?? "";
                        if (emlAddr != "") { RemoveEmail(emlAddr); }
                    }
                }
            }
            else
            {
                WriteToDBLog("Bounce reported from Amazon SNS. Details of the bounced request: " + json);
            }


            return;
        }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public void HandleComplaint() //This method is called from Amazon Simple Notification Service when we receive a complaint.
        {
            string notification = "";
            using (var stream = new MemoryStream())
            {
                var request = HttpContext.Current.Request;
                request.InputStream.Seek(0, SeekOrigin.Begin);
                request.InputStream.CopyTo(stream);
                notification = Encoding.UTF8.GetString(stream.ToArray());
            }

            string json = HandleNotification(notification);
            if (json == "")
            {
                WriteToEventLog("Failed to get JSON event.");
                return;
            }
            if (json == "subscribed")
            {
                WriteToDBLog("Subscribed to the SNS system.");
                return;
            }
            Complaint incComplaint;
            try
            {
                incComplaint = JsonConvert.DeserializeObject<Complaint>(json); //A full list of complaint parameters can be found here: http://docs.aws.amazon.com/ses/latest/DeveloperGuide/notification-contents.html#complaint-object
            }
            catch (Exception ex)
            {
                WriteToEventLog(ex.Message + ex.StackTrace);
                return;
            }
            WriteToDBLog("Complaint reported from Amazon SNS. Details of the complaint notification: " + json);

            foreach (Recipient rec in incComplaint.complainedRecipients)
            {
                string emlAddr = rec.emailAddress ?? "";
                if (emlAddr != "") { RemoveEmail(emlAddr); }
            }

            return;
        }

        private string HandleNotification(string notification)
        {
            string json = "";
            if (notification == "") {
                WriteToEventLog("Received no JSON content from the notification server.");
                return "";
            }
            JObject jOb = JObject.Parse(notification);

            string type = (string)jOb["Type"];
            if (type == "SubscriptionConfirmation") //if we are setting up notification subscription, this should confirm the subscription.
            {
                string url = (string)jOb["SubscribeURL"];
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                Stream resStream = response.GetResponseStream();
                return "subscribed";
            }
            else if (type == "Notification") //if its a notification, we need to figure out whether its a bounce notification or a complaint notification.
            {
                JObject msgObj = JObject.Parse((string)jOb["Message"]);
                string notificationType = (string)msgObj["notificationType"];

                try //put the bounce or complaint json into the json variable and kick it back to the main function.
                {
                    if (notificationType == "Bounce")
                    {
                        json = msgObj["bounce"].ToString();
                    }
                    if (notificationType == "Complaint")
                    {
                        json = msgObj["complaint"].ToString();
                    }
                }
                catch (Exception ex)
                {
                    WriteToEventLog(ex.Message + ex.StackTrace);
                    return "";
                }
            }

           
            return json;
        }

        private void RemoveEmail(string emailAddress)
        {
            //call .net email validation to make sure its a valid email address.
            try
            {
                MailAddress mailAddress = new MailAddress(emailAddress);
            }
            catch
            {
                WriteToEventLog("Invalid email address format.");
            }

            if (IsCellNumber(emailAddress))
            {
                return;
            }
            else
            {
                try
                {
                    ThreadPool.QueueUserWorkItem(delegate { RemoveEmailAllDB(emailAddress); });
                }
                catch
                {
                    WriteToEventLog("Failed to remove the email address: " + emailAddress + " from all databases.");
                }
            }
        }

        private void RemoveEmail(string emailAddress, string databaseName)
        {
            //This is not implemented yet, this method should never be called.
            WriteToDBLog("Removed the email address: " + emailAddress + " from the " + databaseName + " database.");
        }

        private bool IsCellNumber(string emlAddress)
        {

            MailAddress mailAddress = new MailAddress(emlAddress);
            string cellNum = mailAddress.User;
            if (!IsNumber(cellNum) || cellNum.Length!=10)
            {
                return false;
            }

            var cellAC = cellNum.Substring(0, 3);
            var cellPre = cellNum.Substring(3, 3);
            var cellPost = cellNum.Substring(6, 4);

            ThreadPool.QueueUserWorkItem(delegate { RemoveCellAllDB(cellAC,cellPre,cellPost); });

            return true;

        }

        private Boolean IsNumber(String value)
        {
            return value.All(Char.IsDigit);
        }

        private string SendEmail(MailMessage message, string callingAppName)
        {
            try
            {
                // set HTML processing on
                message.IsBodyHtml = true;

                // initialize email client
                using (var client = new SmtpClient(WebConfigurationManager.AppSettings["awshost"], Convert.ToInt32(WebConfigurationManager.AppSettings["awsport"])))
                {
                    ServicePointManager.ServerCertificateValidationCallback = CertificateValidationCallBack;
                    client.Credentials = new NetworkCredential(WebConfigurationManager.AppSettings["awsusername"], WebConfigurationManager.AppSettings["awspassword"]);
                    client.EnableSsl = true;
                    client.Send(message);
                }
                return "";
            }
            catch (SmtpFailedRecipientException ex)
            {
                WriteToDBLog("SmtpFailedRecipientException: " + ex.Message + ex.StackTrace + ". Additional email send info: " + message.To + " " + message.From);
                return "Email Address Invalid";                
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Throttling") || ex.Message.Contains("Service not available") || ex.Message.Contains("Failure sending mail")) //We got throttled, lets delay it a bit and try again.
                {
                    ThreadPool.QueueUserWorkItem(delegate { SleepyEmail(message,0); });
                    WriteToDBLog("Experiencing throttling, attempting to resend...");
                }
                else
                {
                    WriteToDBLog("Non-throttling exception: " + ex.Message + ex.StackTrace + " Additional email send info: " + message.To + " " + message.From + " callingAppName = " + callingAppName);
                }
                return ex.Message + ex.StackTrace;
            }            
        }
        private string SendSMS(MailMessage message, string callingAppName)
        {
           
            // set HTML processing off for phones
            message.IsBodyHtml = false;
            int totalLength = 0;
            int chunkSize = 0;
            string messageString = "";

            if (MMStoSMSChunked(message)) // Some carriers don't support MMS emails. In this case, we need to use their SMS domain and chunk the message into SMS-sized pieces.
            {
                totalLength = totalLength + message.Subject.Length;
                totalLength = totalLength + message.From.DisplayName.ToString().Length;
                totalLength = totalLength + message.Body.Length;

                // Break the message into 137 character sizes for SMS.
                // SMS limit is 153 but we take off 16 characters because phone providers add extra characters in messages.
                //chunkSize = 137 - (message.From.DisplayName.ToString().Length + message.Subject.Length); //This is the amount of room left for the message body.
                message.Subject = "";
                chunkSize = Convert.ToInt32(WebConfigurationManager.AppSettings["ChunkSize"]);

                messageString = message.Body; //We hold the whole message here and chunk it up into multiple messages below.

                for (int i = 0; i < messageString.Length; i += chunkSize)
                {
                    try
                    {
                        // initialize email client
                        message.Body = messageString.Substring(i, Math.Min(chunkSize, messageString.Length - i));
                        using (var client = new SmtpClient(WebConfigurationManager.AppSettings["awshost"], Convert.ToInt32(WebConfigurationManager.AppSettings["awsport"])))
                        {
                            ServicePointManager.ServerCertificateValidationCallback = CertificateValidationCallBack;
                            client.Credentials = new NetworkCredential(WebConfigurationManager.AppSettings["awsusername"], WebConfigurationManager.AppSettings["awspassword"]);
                            client.EnableSsl = true;
                            client.Send(message);
                            Thread.Sleep(1000); //wait 1 second between messages to ensure they come in sequentially.
                        }
                    }
                    catch (SmtpFailedRecipientException ex)
                    {
                        WriteToDBLog("(SendSMS) " + ex.Message + ex.StackTrace + " Additional email send info: " + message.To + " " + message.From);
                        return "Email Address Invalid";
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("Throttling")) //We got throttled, lets delay it a bit and try again.
                        {
                            ThreadPool.QueueUserWorkItem(delegate { SleepyEmail(message, 0); });
                            WriteToDBLog("(SendSMS) Experiencing throttling, attempting to resend...");
                        }
                        else
                        {
                            WriteToDBLog("(SendSMS) Non-throttling exception: " + ex.Message + ex.StackTrace + " Additional email send info: " + message.To + " " + message.From + " callingAppName = " + callingAppName);
                        }
                        return ex.Message + ex.StackTrace;
                    }

                }
            }
            else
            {
                try
                {
                    // initialize email client
                    using (var client = new SmtpClient(WebConfigurationManager.AppSettings["awshost"], Convert.ToInt32(WebConfigurationManager.AppSettings["awsport"])))
                    {
                        ServicePointManager.ServerCertificateValidationCallback = CertificateValidationCallBack;
                        client.Credentials = new NetworkCredential(WebConfigurationManager.AppSettings["awsusername"], WebConfigurationManager.AppSettings["awspassword"]);
                        client.EnableSsl = true;
                        client.Send(message);
                    }
                }
                catch (SmtpFailedRecipientException ex)
                {
                    WriteToDBLog("(SendSMS else) " + ex.Message + ex.StackTrace + " Additional email send info: " + message.To + " " + message.From);
                    return "Email Address Invalid";
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Throttling") || ex.Message.Contains("Service not available") || ex.Message.Contains("Failure sending mail")) //We got throttled, lets delay it a bit and try again.
                    {
                        ThreadPool.QueueUserWorkItem(delegate { SleepyEmail(message, 0); });
                        WriteToDBLog("(SendSMS else) Experiencing throttling, attempting to resend...");
                    }
                    else
                    {
                        WriteToDBLog("(SendSMS else) SMS Sending failure: " + ex.Message + ex.StackTrace + " Additional email send info: " + message.To + " " + message.From);
                    }
                    return ex.Message + ex.StackTrace;
                }
            }
            
            return "";
        }

        private bool MMStoSMSChunked(MailMessage message) //list of domains in which MMS is not supported.
        {

            if (Convert.ToString(WebConfigurationManager.AppSettings["DebugChunkMessagePhoneNo"]).Contains(message.To[0].User.ToString())) return true;

            foreach (var address in message.To)
            {
                switch (address.Host)
                {
                    case "vzwpix.com":
                        return false;
                    case "vtext.com":
                        return Convert.ToBoolean(WebConfigurationManager.AppSettings["ChunkVerizonVTextComSms"]);
                    default:
                        return false;
                }
            }
            return false;
        }

        private void SleepyEmail(MailMessage message, int tries)
        {
            if(tries >= 10)
            {
                WriteToEventLog("Failed to send email after 10 tries, stopping. Message is as follows: " + message.ToString());
                return;
            }
            try
            {
                if (tries < 4)
                {
                    Thread.Sleep(3000); //Wait 3 seconds before trying again.
                }
                else
                {
                    Thread.Sleep(60000); //We are experiencing a much bigger delay than normal, let's hold off for a full minute.
                }
                // initialize email client
                using (var client = new SmtpClient(WebConfigurationManager.AppSettings["awshost"], Convert.ToInt32(WebConfigurationManager.AppSettings["awsport"])))
                {
                    ServicePointManager.ServerCertificateValidationCallback = CertificateValidationCallBack;
                    client.Credentials = new NetworkCredential(WebConfigurationManager.AppSettings["awsusername"], WebConfigurationManager.AppSettings["awspassword"]);
                    client.EnableSsl = true;
                    client.Send(message);
                }
            }
            catch(Exception ex)
            {
                if (ex.Message.Contains("Throttling")) //We got throttled, lets delay it a bit and try again.
                {
                    SleepyEmail(message, tries++); //Recursive
                    WriteToDBLog("Experiencing throttling, attempting to resend... Number of retries: " + tries + ". Message details: " + message.ToString());
                }
                else
                {
                    WriteToEventLog("SleepyEmail has thrown an error. Details: " + ex.Message + ex.StackTrace + " Additional email send info: " + message.To + " " + message.From);
                }
            }
        }

        private void RemoveEmailAllDB(string emlAddr)
        {
            //get all connection strings (located in web config)
            List<string> connectionList = GetConnectionStrings("all");

            //execute the sp_removeemloneachdb for every db in the list to remove all instances of that email address on that db.
            try
            {
                foreach (string connection in connectionList)
                {
                    using (var conn = new SqlConnection(connection))
                    using (var command = new SqlCommand("sp_removeemloneachdb", conn)
                    {
                        CommandType = CommandType.StoredProcedure
                    })
                    {
                        command.Parameters.Add(new SqlParameter("@EmailAddress", emlAddr));
                        command.CommandTimeout = 240;
                        conn.Open();
                        command.ExecuteNonQuery();
                    }
                }
                WriteToDBLog("Removed the email address: " + emlAddr + " from all databases.");
            }
            catch (Exception ex)
            {
                WriteToEventLog("Failed to remove the emaill address: " + emlAddr + " from all databases. DETAILS: " + ex.Message + ex.StackTrace);
                return;
            }
        }

        private void RemoveCellAllDB(string ac, string pre, string post)
        {
            //get all connection strings (located in web config)
            List<string> connectionList = GetConnectionStrings("all");

            //execute the sp_removeemloneachdb for every db in the list to remove all instances of that email address on that db.
            try
            {
                foreach (string connection in connectionList)
                {
                    using (var conn = new SqlConnection(connection))
                    using (var command = new SqlCommand("sp_removecelloneachdb", conn)
                    {
                        CommandType = CommandType.StoredProcedure
                    })
                    {
                        command.Parameters.Add(new SqlParameter("@AC", ac));
                        command.Parameters.Add(new SqlParameter("@Pre", pre));
                        command.Parameters.Add(new SqlParameter("@Post", post));
                        command.CommandTimeout = 240;
                        conn.Open();
                        command.ExecuteNonQuery();
                    }
                }
                WriteToDBLog("Removed the cell provider for: " + ac + pre + post + " from all databases.");
            }
            catch (Exception ex)
            {
                WriteToEventLog("Failed to remove the cell provider for: " + ac + pre + post + " from all databases. DETAILS: " + ex.Message + ex.StackTrace);
                return;
            }
        }

        private List<string> GetConnectionStrings(string dbname)
        {
            List<string> connectionStrings = new List<string>();
            if (dbname=="all")
            {
                connectionStrings.Add(WebConfigurationManager.AppSettings["DB0Master"]);
                connectionStrings.Add(WebConfigurationManager.AppSettings["DB1Master"]);
                connectionStrings.Add(WebConfigurationManager.AppSettings["DB2Master"]);
                connectionStrings.Add(WebConfigurationManager.AppSettings["DB3Master"]);
                connectionStrings.Add(WebConfigurationManager.AppSettings["DB4Master"]);
            }
            else
            {

            }
            return connectionStrings;
        }
        
        private static void WriteToEventLog(string message)
        {
            EventLog.WriteEntry("AWS Email Web Service", message, EventLogEntryType.Error, 1);
        }        
        private static void WriteToDBLog(string message)
        {
            var connection = WebConfigurationManager.AppSettings["DBLog"];
            using (var conn = new SqlConnection(connection))
            using (var command = new SqlCommand("LogAction", conn)
            {
                CommandType = CommandType.StoredProcedure
            })
            {
                command.Parameters.Add(new SqlParameter("@Message", message));
                command.Parameters.Add(new SqlParameter("@Software", "Email Web Service"));
                conn.Open();
                command.ExecuteNonQuery();
            }
        }

        private static bool CertificateValidationCallBack(
         object sender,
         System.Security.Cryptography.X509Certificates.X509Certificate certificate,
         System.Security.Cryptography.X509Certificates.X509Chain chain,
         System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            
            // If the certificate is a valid, signed certificate, return true.
            if (sslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
            {
                return true;
            }

            // If there are errors in the certificate chain, look at each error to determine the cause.
            if ((sslPolicyErrors & System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            {
                if (chain != null && chain.ChainStatus != null)
                {
                    foreach (System.Security.Cryptography.X509Certificates.X509ChainStatus status in chain.ChainStatus)
                    {
                        string chainelements = "";
                        foreach (var elem in chain.ChainElements)
                        {
                            chainelements = chainelements + elem.Certificate + " -|- ";
                            foreach (var whatev in elem.ChainElementStatus)
                            {
                                chainelements = chainelements + whatev.StatusInformation + " - " + whatev.Status + " -|- ";
                            }
                        }
                        
                        if ((certificate.Subject == certificate.Issuer) &&
                           (status.Status == System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.UntrustedRoot))
                        {
                            // Self-signed certificates with an untrusted root are valid. 
                            continue;
                        }
                        else
                        {
                            if (status.Status != System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.NoError)
                            {
                                // If there are any other errors in the certificate chain, the certificate is invalid,
                                // so the method returns false.
                                return false;
                            }
                        }
                    }
                }

                // When processing reaches this line, the only errors in the certificate chain are 
                // untrusted root errors for self-signed certificates. These certificates are valid
                // for default Exchange server installations, so return true.
                return true;
            }
            else
            {

                // In all other cases, return false.
                return false;
            }
        }

        private bool IsCell(string email)
        {
            var isCell = false;

            try
            {
                var addr = new System.Net.Mail.MailAddress(email);

                string cellNum = addr.User;
                if (IsNumber(cellNum) && cellNum.Length == 10)
                {
                    isCell = true;
                }
            }
            catch
            {
                return false; // invalid email
            }

            return IsValidEmail(email) && isCell;
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

    }
}
