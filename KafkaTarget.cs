using Confluent.Kafka;
using Newtonsoft.Json;
using NLog.Common;
using NLog.Config;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NLog.Kafka
{
    [Target("Kafka")]
    public class KafkaTarget : TargetWithLayout
    {
        private IProducer<string, string> _producer = null;

        public KafkaTarget()
        {
            this.ProducerConfigs = new List<NLogProducerConfig>(10);
        }


        [RequiredParameter]
        [ArrayParameter(typeof(NLogProducerConfig), "producerConfig")]
        public IList<NLogProducerConfig> ProducerConfigs { get; set; }

        [RequiredParameter]
        public string appname { get; set; }

        [RequiredParameter]
        public string topic { get; set; }

        [RequiredParameter]
        public bool includeMdc { get; set; }



        protected override void Write(LogEventInfo logEvent)
        {
            IPHostEntry ipHost = Dns.GetHostEntryAsync(Dns.GetHostName()).Result;
            IPAddress ipAddr = ipHost.AddressList[0];

            Dictionary<string, object> formatLogEvent = new Dictionary<string, object>() {
                { "version"     , logEvent.SequenceID },
                { "@timestamp"  , DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) },
                { "appname"     , this.appname },
                { "topic"     , this.topic },
                { "HOSTNAME"    , ipAddr.ToString() },
                { "thread_name" , System.Threading.Thread.CurrentThread.Name },
                { "level"       , logEvent.Level.Name },
                { "logger_name" , logEvent.LoggerName },
                { "log_msg"     , logEvent.FormattedMessage },
            };



            if (logEvent.Exception != null)
            {
                formatLogEvent["message"] = logEvent.Exception.Message;
                formatLogEvent["stack_trace"] = logEvent.Exception.StackTrace;
            }


            if (includeMdc)
            {
                TransferContextDataToLogEventProperties(formatLogEvent);
            }

            string message = JsonConvert.SerializeObject(formatLogEvent);

            SendMessageToQueue(message);

            base.Write(logEvent);
        }


        private static void TransferContextDataToLogEventProperties(Dictionary<string, object> logDic)
        {
            foreach (var contextItemName in MappedDiagnosticsContext.GetNames())
            {
                var key = contextItemName;

                if (!logDic.ContainsKey(key))
                {
                    var value = MappedDiagnosticsContext.Get(key);
                    logDic.Add(key, value);
                }
            }
        }

        #region 创建 kafka 与 发现队列函数
        private IProducer<string, string> GetProducerNew()
        {
            if (this.ProducerConfigs == null || this.ProducerConfigs.Count == 0) throw new Exception("ProducerConfigs is not found");

            if (_producer == null)
            {

                var config = new ProducerConfig();

                foreach (var pconfig in this.ProducerConfigs)
                {
                    switch (pconfig.Key)
                    {
                        case "bootstrap.servers":
                            config.BootstrapServers = pconfig.value;
                            break;
                        case "queue.buffering.max.messages":
                            config.QueueBufferingMaxMessages = int.Parse(pconfig.value);
                            break;
                        case "retry.backoff.ms":
                            config.RetryBackoffMs = int.Parse(pconfig.value);
                            break;
                        case "message.send.max.retries":
                            config.MessageSendMaxRetries = int.Parse(pconfig.value);
                            break;
                        case "request.timeout.ms":
                            config.RequestTimeoutMs = int.Parse(pconfig.value);
                            break;
                    }
                }

                _producer = new ProducerBuilder<string, string>(config).Build();

            }
            return _producer;
        }
        DateTime? errorTime;
        private async Task SendMessageToQueue(string message)
        {

            try
            {
                //发消息异常了 就60s 内禁止 发送消息,避免异常堆积
                if (errorTime.HasValue && (DateTime.Now - errorTime.Value).TotalSeconds < 5 * 60)
                {
                    Console.WriteLine($"距离可发送时间(上次发送出现错误)，还有 {(DateTime.Now - errorTime.Value).TotalSeconds}-60秒");
                    return;

                }
                if (string.IsNullOrEmpty(message))
                    return;
                var producer = this.GetProducerNew();
                var key = "Multiple." + DateTime.Now.Ticks;

                try
                {
                    var dr = await producer.ProduceAsync(topic, new Message<string, string> { Value = message });
                }
                catch (ProduceException<Null, string> e)
                {
                    errorTime = DateTime.Now;
                    Console.WriteLine($"Delivery failed: {e.Error.Reason}");
                }

            }
            catch (Exception ex)
            {
                errorTime = DateTime.Now;
                Console.WriteLine($"发送消息异常：{ex.Message}{ex.StackTrace}");
            }

        }
       
        private void CloseProducer()
        {
            if (_producer != null)
            {
                _producer?.Flush(TimeSpan.FromSeconds(60));
                _producer?.Dispose();
            }
            _producer = null;
        }

        #endregion


        protected override void CloseTarget()
        {
            CloseProducer();
            base.CloseTarget();
        }

    }
}
