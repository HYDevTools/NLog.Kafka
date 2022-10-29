using NLog.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NLog.Kafka
{
    [NLogConfigurationItem]
    public class NLogProducerConfig
    {
        [RequiredParameter]
        public string Key { get; set; }

        [RequiredParameter]
        public string value { set; get; }
    }
}
