using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisualVersionofService.Comunications;

namespace VisualVersionofService
{
    public partial class Form1 : Form
    {
        public static Form1 MainForm;

        public Form1()
        {
            InitializeComponent();
            ColumnHeader columnHeader1 = new ColumnHeader();
            columnHeader1.Text = "Text";
            this.DiagnosticListView.Columns.AddRange(new ColumnHeader[] { columnHeader1 });
            this.DiagnosticListView.View = View.List;
            MainForm = this;
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            Task.Run(() => OnStart());
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            Task.Run(() => OnStop());
        }

        private void DiagnosticOut(string message)
        {
            try
            {
                MainForm.Invoke((MethodInvoker)delegate
                {
                    ListViewItem item = new ListViewItem(message);
                    MainForm.DiagnosticListView.Items.Add(item);
                });
            }
            catch (Exception ex) { DiagnosticOut(ex.ToString()); }
        }

        //above is winforms specific code. below should be portable to service.
        private NetworkStream CamstarStream;//Stream for the main connection to and from camstar

        private TcpClient CamstarClient;//Main Connection to and from camstar
        private TopicPublisher TestPublisher;//publishes to the Pac-Light Outbound topic
        private TopicSubscriber MainInputSubsriber;//Main subscriber subs to SNP.Inbound
        private SqlConnection ENGDBConnection;//Connection to the ENGDB default db is SNPDb.
        private UdpClient MDEClient;

        private const string SubTopicName = "SNP.Inbound";
        private const string TestTopicName = "SNP.Outbound";
        private const string Broker = "tcp://10.197.10.32:61616";
        private const string ClientID = "SNPService";
        private const string ConsumerID = "SNPService";
        private const string CamstarUsername = "camstaruser";
        private const string CamstarPassword = "c@mst@rus3r";
        private const string QACamstarIP = "10.197.10.32";
        private const string ProdCamstarIP = "0.0.0.0";
        private const string ProdENG_DBDataSource = "10.197.10.26";
        private const string QAENG_DBDataSource = "10.197.10.37";
        private const string ENG_DBUserID = "camstaruser";
        private const string ENG_DBPassword = "c@mst@rus3r";
        private const string ENG_DBInitialCatalog = "Pac-LiteDb";
        private const string MDEIP = "0.0.0.0";
        private const Int32 CamstarPort = 2881;
        private const Int32 MDEClientPort = 11000;
        private const Int32 MDEPort = 0;
        private List<Disposable> ThingsToDispose;//whenever you make a connection/connection based stream add it to this.

        private delegate void SetTextCallback(string text);

        /// <summary>
        ///  called whenever a mqtt message from SNP is received
        /// </summary>
        private void MainInputSubsriber_OnMessageReceived(string Message)
        {
            DiagnosticOut(Message);//log message and bits when it comes in.
            DiagnosticOut("Packet Header =" + Convert.ToInt32(Message[0]).ToString());
            DiagnosticOut("Packet Type=" + Convert.ToInt32(Message[1]).ToString());
            DiagnosticOut("SNPID=" + Convert.ToInt32(Message[2]).ToString());
            switch (Convert.ToInt32(Message[0]))//switch packet header
            {
                case 1://this means its a SNP message

                    switch (Convert.ToInt32(Message[1]))//switch Packet Type
                    {
                        //run the procedure in the background dont await as we dont need the return values as it should be void.
                        case 1:
                            Task.Run(() => IndexSummaryPacket(Message));
                            break;

                        case 2:
                            Task.Run(() => DowntimePacket(Message));
                            break;

                        case 3:
                            Task.Run(() => ShortTimeStatisticPacket(Message));
                            break;

                        case 252:
                            Task.Run(() => CamstarTestPacket(Message));
                            break;

                        case 253:
                            Task.Run(() => ActiveMQTestPacket(Message));
                            break;

                        case 254:
                            Task.Run(() => SQLTestPacket(Message));
                            break;

                        default:
                            break;
                    }
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// SQL section of the Fifteen Minut Packet Inserts the data into a table named after the machine
        /// </summary>
        private void SQLIndexSummary(string Message)
        {
            try //try loop in case command fails.
            {
                string JsonString = Message.Substring(7, Message.Length - 7);//grab json data from the end.
                JObject ReceivedPacket = JsonConvert.DeserializeObject(JsonString) as JObject;
                StringBuilder SQLStringBuilder = new StringBuilder();
                SQLStringBuilder.Append("INSERT INTO " + ReceivedPacket["Machine"] + "(");
                IList<string> keys = ReceivedPacket.Properties().Select(p => p.Name).ToList();//gets list of all keys in json object
                string KeySection = "";
                string ValueSection = "";
                foreach (string key in keys)//foreach key
                {
                    if (key == "UOM" || key == "Good" || key == "NAED" || key == "Bad" || key == "Empty" || key == "MachineIndexes")//except machine as it is used as the table name.
                    {
                        KeySection += key + ", ";//Make a key
                        ValueSection += "@" + key + ", ";//and value Reference to be replaced later
                    }
                }
                KeySection += "Time, ";//Make a Time key
                ValueSection += "@Time, ";//and value Reference to be replaced later
                KeySection += "MachineID ";
                ValueSection += "MachineID ";
                SQLStringBuilder.Append(KeySection + ")");
                SQLStringBuilder.Append("SELECT " + ValueSection + "from MachineInfoTable" + " where MachineName = @Machine ;");//append both to the command string
                string SQLString = SQLStringBuilder.ToString();//convert to string
                using (SqlCommand command = new SqlCommand(SQLString, ENGDBConnection))
                {
                    foreach (string key in keys)//foreach key
                    {
                        switch (key)
                        {
                            case "UOM":
                                command.Parameters.AddWithValue("@" + key, ReceivedPacket[key].ToString());
                                break;

                            case "NAED":
                                command.Parameters.AddWithValue("@" + key, ReceivedPacket[key].ToString());
                                break;

                            case "Good":
                                command.Parameters.AddWithValue("@" + key, Convert.ToInt32(ReceivedPacket[key]));
                                break;

                            case "Bad":
                                command.Parameters.AddWithValue("@" + key, Convert.ToInt32(ReceivedPacket[key]));
                                break;

                            case "Empty":
                                command.Parameters.AddWithValue("@" + key, Convert.ToInt32(ReceivedPacket[key]));
                                break;

                            case "MachineIndexes":
                                command.Parameters.AddWithValue("@" + key, Convert.ToInt32(ReceivedPacket[key]));
                                break;

                            default:
                                break;
                        }
                    }
                    command.Parameters.AddWithValue("@Time", DateTime.Now);
                    command.Parameters.AddWithValue("@Machine", ReceivedPacket["Machine"].ToString());
                    int rowsAffected = command.ExecuteNonQuery();// execute the command returning number of rows affected
                    DiagnosticOut(rowsAffected + " row(s) inserted");//logit
                }
            }
            catch (Exception ex) { DiagnosticOut(ex.ToString()); }
        }

        /// <summary>
        /// Camstar section of the Fifteen Minut Packet sends a throughput packet for the resource named for the machine.
        /// </summary>
        private void CamstarIndexSummary(string Message)
        {
            try
            {
                string JsonString = Message.Substring(7, Message.Length - 7);//grab json data from the end.
                JObject ReceivedPacket = JsonConvert.DeserializeObject(JsonString) as JObject;
                StringBuilder PacketStringBuilder = new StringBuilder();
                PacketStringBuilder.Append("<__InSite __version=\"1.1\" __encryption=\"2\"><__session><__connect><user><__name>");
                PacketStringBuilder.Append(CamstarUsername);//username
                PacketStringBuilder.Append("</__name></user><password __encrypted=\"no\">");
                PacketStringBuilder.Append(CamstarPassword);//password
                PacketStringBuilder.Append("</password></__connect><__filter><__allowUntaggedInstances><![CDATA[3]]></__allowUntaggedInstances></__filter></__session><__service __serviceType=\"ResourceThruput\"><__utcOffset><![CDATA[-04:00:00]]></__utcOffset><__inputData><MfgOrder><__name><![CDATA[]]></__name></MfgOrder><Product><__name><![CDATA[");
                PacketStringBuilder.Append(ReceivedPacket["Naed"]);//productNaed
                PacketStringBuilder.Append("]]></__name><__useROR><![CDATA[true]]></__useROR></Product><Qty><![CDATA[");
                PacketStringBuilder.Append(ReceivedPacket["Good"]);//qty
                PacketStringBuilder.Append("]]></Qty><Resource><__name><![CDATA[");
                PacketStringBuilder.Append(ReceivedPacket["Machine"]);//resource
                PacketStringBuilder.Append("]]></__name></Resource><ResourceGroup><__name><![CDATA[");
                PacketStringBuilder.Append(ReceivedPacket["Line"]);//resourceGroup
                PacketStringBuilder.Append("]]></__name></ResourceGroup><UOM><__name><![CDATA[");
                PacketStringBuilder.Append(ReceivedPacket["UOM"]);//UOM
                PacketStringBuilder.Append("]]></__name>/</UOM></__inputData><__perform><__eventName><![CDATA[GetWIPMsgs]]></__eventName></__perform><__requestData><CompletionMsg /><WIPMsgMgr><WIPMsgs><AcknowledgementRequired /><MsgAcknowledged /><MsgText /><PasswordRequired /><WIPMsgDetails /></WIPMsgs></WIPMsgMgr></__requestData></__service></__InSite>");
            }
            catch (Exception ex) { DiagnosticOut(ex.ToString()); }
        }

        /// <summary>
        /// Summary packet received every fifteen minutes from the plc.
        /// </summary>
        private void IndexSummaryPacket(string Message)
        {
            DiagnosticOut("Fifteen Minute Packet Received!");
            Task.Run(() => SQLIndexSummary(Message));
            Task.Run(() => CamstarIndexSummary(Message));
        }

        private void SQLDownTimePacket(string Message)
        {
            try //try loop in case command fails.
            {
                string JsonString = Message.Substring(7, Message.Length - 7);//grab json data from the end.
                JObject ReceivedPacket = JsonConvert.DeserializeObject(JsonString) as JObject;
                StringBuilder SQLStringBuilder = new StringBuilder();
                SQLStringBuilder.Append("INSERT INTO MachineDownTimes (");
                IList<string> keys = ReceivedPacket.Properties().Select(p => p.Name).ToList();//gets list of all keys in json object
                string KeySection = "";
                string ValueSection = "";
                foreach (string key in keys)//foreach key
                {
                    if (key == "UpTime" || key == "DownTime" || key == "NAED" || key == "MachineReason" || key == "UserReason")//except machine as it is used as the table name.
                    {
                        KeySection += key + ", ";//Make a key
                        ValueSection += "@" + key + ", ";//and value Reference to be replaced later
                    }
                }
                KeySection += "MachineID ";
                ValueSection += "MachineID ";
                SQLStringBuilder.Append(KeySection + ")");
                SQLStringBuilder.Append("SELECT " + ValueSection + "from MachineInfoTable" + " where MachineName = @Machine ;");//append both to the command string
                string SQLString = SQLStringBuilder.ToString();//convert to string
                using (SqlCommand command = new SqlCommand(SQLString, ENGDBConnection))
                {
                    foreach (string key in keys)//foreach key
                    {
                        switch (key)
                        {
                            case "UpTime":
                                string uptime = ReceivedPacket[key].ToString();
                                int upday = Convert.ToInt32(uptime.Substring(0, 2));
                                int uphour = Convert.ToInt32(uptime.Substring(3, 2));
                                int upminute = Convert.ToInt32(uptime.Substring(6, 2));
                                int upsecond = Convert.ToInt32(uptime.Substring(9, 2));
                                command.Parameters.AddWithValue("@" + key, new DateTime(DateTime.Now.Year, DateTime.Now.Month, upday, uphour, upminute, upsecond));
                                break;

                            case "DownTime":
                                string Downtime = ReceivedPacket[key].ToString();
                                int Downday = Convert.ToInt32(Downtime.Substring(0, 2));
                                int Downhour = Convert.ToInt32(Downtime.Substring(3, 2));
                                int Downminute = Convert.ToInt32(Downtime.Substring(6, 2));
                                int Downsecond = Convert.ToInt32(Downtime.Substring(9, 2));
                                command.Parameters.AddWithValue("@" + key, new DateTime(DateTime.Now.Year, DateTime.Now.Month, Downday, Downhour, Downminute, Downsecond));
                                break;

                            case "NAED":
                                command.Parameters.AddWithValue("@" + key, ReceivedPacket[key].ToString());
                                break;

                            case "MachineReason":
                                command.Parameters.AddWithValue("@" + key, ReceivedPacket[key].ToString());
                                break;

                            case "UserReason":
                                command.Parameters.AddWithValue("@" + key, ReceivedPacket[key].ToString());
                                break;

                            default:
                                break;
                        }
                    }
                    command.Parameters.AddWithValue("@Machine", ReceivedPacket["Machine"].ToString());
                    int rowsAffected = command.ExecuteNonQuery();// execute the command returning number of rows affected
                    DiagnosticOut(rowsAffected + " row(s) inserted");//logit
                }
            }
            catch (Exception ex) { DiagnosticOut(ex.ToString()); }
        }

        /// <summary>
        ///  Packet sent each time there is a Downtime received from SNP
        /// </summary>
        private void DowntimePacket(string Message)
        {
            DiagnosticOut("DownTime Packet Received!");
            Task.Run(() => SQLDownTimePacket(Message));//dont care about return.
        }

        /// <summary>
        ///  Packet sent at each index
        /// </summary>
        private void ShortTimeStatisticPacket(string Message)
        {
            DiagnosticOut("Short Time Statistic Packet Received!");
            Task.Run(() => SQLShortTimeStatisticPacket(Message));
            Task.Run(() => MDEShortTimeStatisticPacket(Message));
        }

        /// <summary>
        ///  Packet sent at each index to sql
        /// </summary>
        private void SQLShortTimeStatisticPacket(string Message)
        {
            try //try loop in case command fails.
            {
                string JsonString = Message.Substring(7, Message.Length - 7);//grab json data from the end.
                JObject ReceivedPacket = JsonConvert.DeserializeObject(JsonString) as JObject;
                StringBuilder SQLStringBuilder = new StringBuilder();
                SQLStringBuilder.Append("INSERT INTO " + ReceivedPacket["Machine"].ToString() + "ShortTimeStatistics (");
                IList<string> keys = ReceivedPacket.Properties().Select(p => p.Name).ToList();//gets list of all keys in json object
                string KeySection = "[";
                string ValueSection = "";
                foreach (string key in keys)//foreach key
                {
                    if (key != "Machine")//except machine as it is used as the table name.
                    {
                        KeySection += key + "], [";//Make a key
                        ValueSection += "@" + key + ", ";//and value Reference to be replaced later
                    }
                }
                KeySection += "MachineID] ";
                ValueSection += "MachineID ";
                SQLStringBuilder.Append(KeySection + ")");
                SQLStringBuilder.Append("SELECT " + ValueSection + "from MachineInfoTable" + " where MachineName = @Machine ;");//append both to the command string
                string SQLString = SQLStringBuilder.ToString();//convert to string
                using (SqlCommand command = new SqlCommand(SQLString, ENGDBConnection))
                {
                    foreach (string key in keys)//foreach key
                    {
                        if (key != "Machine")
                        {
                            command.Parameters.AddWithValue("@" + key, 1 == Convert.ToInt32(ReceivedPacket[key]));
                        }
                    }
                    command.Parameters.AddWithValue("@Machine", ReceivedPacket["Machine"].ToString());
                    int rowsAffected = command.ExecuteNonQuery();// execute the command returning number of rows affected
                    DiagnosticOut(rowsAffected + " row(s) inserted");//logit
                }
            }
            catch (Exception ex) { DiagnosticOut(ex.ToString()); }
        }

        /// <summary>
        ///  Packet sent at each index to MDE over UDP
        /// </summary>
        private void MDEShortTimeStatisticPacket(string Message)
        {
        }

        /// <summary>
        ///  Test Packet Heading to Camstar with XML
        /// </summary>
        private void CamstarTestPacket(string Message)//camstar xml is the messiest packet due to the XML they are expecting
        {
            DiagnosticOut("CamstarTestPacket Received!");
            try
            {
                string JsonString = Message.Substring(7, Message.Length - 7);//grab json data from the end.
                JObject ReceivedPacket = JsonConvert.DeserializeObject(JsonString) as JObject;
                StringBuilder PacketStringBuilder = new StringBuilder();
                PacketStringBuilder.Append("<__InSite __version=\"1.1\" __encryption=\"2\"><__session><__connect><user><__name>");
                PacketStringBuilder.Append(CamstarUsername);//username
                PacketStringBuilder.Append("</__name></user><password __encrypted=\"no\">");
                PacketStringBuilder.Append(CamstarPassword);//password
                PacketStringBuilder.Append("</password></__connect><__filter><__allowUntaggedInstances><![CDATA[3]]></__allowUntaggedInstances></__filter></__session><__service __serviceType=\"ResourceThruput\"><__utcOffset><![CDATA[-04:00:00]]></__utcOffset><__inputData><MfgOrder><__name><![CDATA[]]></__name></MfgOrder><Product><__name><![CDATA[");
                PacketStringBuilder.Append(ReceivedPacket["Naed"]);//productNaed
                PacketStringBuilder.Append("]]></__name><__useROR><![CDATA[true]]></__useROR></Product><Qty><![CDATA[");
                PacketStringBuilder.Append(ReceivedPacket["Good"]);//qty
                PacketStringBuilder.Append("]]></Qty><Resource><__name><![CDATA[");
                PacketStringBuilder.Append(ReceivedPacket["Machine"]);//resource
                PacketStringBuilder.Append("]]></__name></Resource><ResourceGroup><__name><![CDATA[");
                PacketStringBuilder.Append(ReceivedPacket["Line"]);//resourceGroup
                PacketStringBuilder.Append("]]></__name></ResourceGroup><UOM><__name><![CDATA[");
                PacketStringBuilder.Append(ReceivedPacket["UOM"]);//UOM
                PacketStringBuilder.Append("]]></__name>/</UOM></__inputData><__perform><__eventName><![CDATA[GetWIPMsgs]]></__eventName></__perform><__requestData><CompletionMsg /><WIPMsgMgr><WIPMsgs><AcknowledgementRequired /><MsgAcknowledged /><MsgText /><PasswordRequired /><WIPMsgDetails /></WIPMsgs></WIPMsgMgr></__requestData></__service></__InSite>");
            }
            catch (Exception ex) { DiagnosticOut(ex.ToString()); }
        }

        /// <summary>
        ///  Test Packet Heading to ActiveMQ with MQTT
        /// </summary>
        private void ActiveMQTestPacket(string Message)
        {
            DiagnosticOut("ActiveMQTestPacketReceived!");
            try
            {
                string JsonString = Message.Substring(7, Message.Length - 7);//grab json data from the end.
                JObject ReceivedPacket = JsonConvert.DeserializeObject(JsonString) as JObject;
                //do whatever to the data before sending it on

                //v append message header then re serialize object and send. topic setup in publisher.
                TestPublisher.SendMessage(Message.Substring(0, 7) + JsonConvert.SerializeObject(ReceivedPacket));
            }
            catch (Exception ex) { DiagnosticOut(ex.ToString()); }
        }

        /// <summary>
        ///  Test Packet Heading to ENGDB with SQL
        /// </summary>
        private void SQLTestPacket(string Message)
        {
            DiagnosticOut("SQL Test Packet Received!");
            try //try loop in case command fails.
            {
                string JsonString = Message.Substring(7, Message.Length - 7);//grab json data from the end.
                JObject ReceivedPacket = JsonConvert.DeserializeObject(JsonString) as JObject;
                StringBuilder SQLStringBuilder = new StringBuilder();
                SQLStringBuilder.Append("INSERT INTO " + ReceivedPacket["Machine"] + "(");
                IList<string> keys = ReceivedPacket.Properties().Select(p => p.Name).ToList();//gets list of all keys in json object
                string KeySection = "";
                string ValueSection = "";
                foreach (string key in keys)//foreach key
                {
                    if (key == "UOM" || key == "Good" || key == "NAED" || key == "Bad" || key == "Empty" || key == "MachineIndexes")//except machine as it is used as the table name.
                    {
                        KeySection += key + ", ";//Make a key
                        ValueSection += "@" + key + ", ";//and value Reference to be replaced later
                    }
                }
                KeySection += "Time, ";//Make a Time key
                ValueSection += "@Time, ";//and value Reference to be replaced later
                KeySection += "MachineID ";
                ValueSection += "MachineID ";
                SQLStringBuilder.Append(KeySection + ")");
                SQLStringBuilder.Append("SELECT " + ValueSection + "from MachineInfoTable" + " where MachineName = @Machine ;");//append both to the command string
                string SQLString = SQLStringBuilder.ToString();//convert to string
                using (SqlCommand command = new SqlCommand(SQLString, ENGDBConnection))
                {
                    foreach (string key in keys)//foreach key
                    {
                        switch (key)
                        {
                            case "UOM":
                                command.Parameters.AddWithValue("@" + key, ReceivedPacket[key].ToString());
                                break;

                            case "NAED":
                                command.Parameters.AddWithValue("@" + key, ReceivedPacket[key].ToString());
                                break;

                            case "Good":
                                command.Parameters.AddWithValue("@" + key, Convert.ToInt32(ReceivedPacket[key]));
                                break;

                            case "Bad":
                                command.Parameters.AddWithValue("@" + key, Convert.ToInt32(ReceivedPacket[key]));
                                break;

                            case "Empty":
                                command.Parameters.AddWithValue("@" + key, Convert.ToInt32(ReceivedPacket[key]));
                                break;

                            case "MachineIndexes":
                                command.Parameters.AddWithValue("@" + key, Convert.ToInt32(ReceivedPacket[key]));
                                break;

                            default:
                                break;
                        }
                    }
                    command.Parameters.AddWithValue("@Time", DateTime.Now);
                    command.Parameters.AddWithValue("@Machine", ReceivedPacket["Machine"].ToString());
                    int rowsAffected = command.ExecuteNonQuery();// execute the command returning number of rows affected
                    DiagnosticOut(rowsAffected + " row(s) inserted");//logit
                }
            }
            catch (Exception ex) { DiagnosticOut(ex.ToString()); }
        }

        /// <summary>
        /// Collection of MQTT Connection setup
        /// </summary>
        private void MQTTConnections()
        {
            try
            {
                DiagnosticOut("Connecting MainSubscriber");
                MainInputSubsriber = new TopicSubscriber(SubTopicName, Broker, ClientID, ConsumerID);
                MainInputSubsriber.OnMessageReceived += new MessageReceivedDelegate(MainInputSubsriber_OnMessageReceived);
                ThingsToDispose.Add(new Disposable(nameof(MainInputSubsriber), MainInputSubsriber));//add to reference pile so it disposes of itself properly.
            }
            catch (Exception ex) { DiagnosticOut(ex.ToString()); }
            try
            {
                DiagnosticOut("Connecting TestPublisher");
                TestPublisher = new TopicPublisher(TestTopicName, Broker);
                ThingsToDispose.Add(new Disposable(nameof(TestPublisher), TestPublisher));
            }
            catch (Exception ex) { DiagnosticOut(ex.ToString()); }
        }

        /// <summary>
        /// Collection of SQL Connection setup
        /// </summary>
        private void SQLConnections()
        {
            try
            {
                // Build connection string
                DiagnosticOut("Connecting SQL Database");
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
                builder.DataSource = QAENG_DBDataSource;
                builder.UserID = ENG_DBUserID;
                builder.Password = ENG_DBPassword;
                builder.InitialCatalog = ENG_DBInitialCatalog;
                // Connect to SQL
                Console.Write("Connecting to SQL Server ... ");
                ENGDBConnection = new SqlConnection(builder.ConnectionString);
                ENGDBConnection.Open();
                ThingsToDispose.Add(new Disposable(nameof(ENGDBConnection), ENGDBConnection));
            }
            catch (Exception ex) { DiagnosticOut(ex.ToString()); }
        }

        /// <summary>
        /// Collection of TCP Connection setup
        /// </summary>
        private void TCPConnections()
        {
            DiagnosticOut("Connecting TCP to Camstar");
            try
            {
                CamstarClient = new TcpClient(QACamstarIP, CamstarPort);//connect
                CamstarStream = CamstarClient.GetStream();//get stream to read and write to.
                ThingsToDispose.Add(new Disposable(nameof(CamstarClient), CamstarClient));
                ThingsToDispose.Add(new Disposable(nameof(CamstarStream), CamstarStream));
            }
            catch (Exception ex) { DiagnosticOut(ex.ToString()); }
        }

        /// <summary>
        /// Collection of UDP Connection setup
        /// </summary>
        private void UDPConnections()
        {
            DiagnosticOut("Connecting to MDE");
            try
            {
                MDEClient = new UdpClient(MDEClientPort);
                MDEClient.Connect(MDEIP, MDEPort);
                ThingsToDispose.Add(new Disposable(nameof(MDEClient), MDEClient));
                ThingsToDispose.Add(new Disposable(nameof(CamstarStream), CamstarStream));
            }
            catch (Exception ex) { DiagnosticOut(ex.ToString()); }
        }

        /// <summary>
        /// Call On stop of service
        /// </summary>
        private void OnStop()
        {
            MDEClient.Close();//close UDP connections to.
            foreach (Disposable disposable in ThingsToDispose)
            {
                try
                {
                    disposable.Dispose();//dispose of connections on stop they will be reestablished on start.
                    DiagnosticOut(disposable.Name + "Has been Disconected and Disposed");
                }
                catch (Exception ex) { DiagnosticOut(disposable.Name + ex.ToString()); }
            }
        }

        /// <summary>
        /// Call On start of service
        /// </summary>
        private void OnStart()
        {
            ThingsToDispose = new List<Disposable>();
            Task.Run(() => MQTTConnections());//open all MQTT Connections
            Task.Run(() => SQLConnections());//open alll SQL Connections
            Task.Run(() => TCPConnections());//open all TCPConnections
            Task.Run(() => UDPConnections());//open all UDP Connections
        }
    }
}