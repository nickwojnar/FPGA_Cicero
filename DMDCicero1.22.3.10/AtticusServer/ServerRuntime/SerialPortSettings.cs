using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using NationalInstruments.Visa;
using Ivi.Visa;

namespace AtticusServer
{
    /// <summary>
    /// Used to set baud rates etc. of serial ports.
    /// </summary>
    [Serializable, TypeConverter(typeof(ExpandableObjectConverter))]
    public class SerialPortSettings
    {
        private string portName;

        public string PortName
        {
            get { return portName; }
            set { portName = value; }
        }

        private int baudRate;

        public int BaudRate
        {
            get { return baudRate; }
            set { baudRate = value; }
        }

        private short dataBits;

        public short DataBits
        {
            get { return dataBits; }
            set { dataBits = value; }
        }

        private SerialParity myParity;

        public SerialParity parity
        {
            get { return myParity; }
            set { myParity = value; }
        }

        private SerialStopBitsMode stopBits;

        public SerialStopBitsMode StopBits
        {
            get { return stopBits; }
            set { stopBits = value; }
        }

        private SerialFlowControlModes flowControl;

        public SerialFlowControlModes FlowControl
        {
            get { return flowControl; }
            set { flowControl = value; }
        }



    }

}
