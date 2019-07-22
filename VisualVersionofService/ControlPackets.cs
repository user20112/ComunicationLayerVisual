using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisualVersionofService
{
    class ControlPackets
    {        /// <summary>
             /// Packet Sent every index for the EMP system. Simply insert into SQL for recording ( and grab a time stamp if missing)
             /// </summary>
        public void LoggingLevel(string message)
        {
        }
        public void Silence(string message)
        {
        }
        public void Listen(string message)
        {
        }
    }
}
