using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Szab.BeatDetector
{
    class BassWasapiInitException : Exception
    {
        public BassWasapiInitException(String message)
            : base(message)
        { }

        public override string Message
        {
            get
            {
                return "Wystąpił błąd podczas inicjalizowania WASAPI. " + base.Message;
            }
        }
    }

    class BassInitException : Exception
    {
        public BassInitException(String message)
            : base(message)
        { }

        public override string Message
        {
            get
            {
                return "Wystąpił błąd podczas inicjalizowania BASS.NET. " + base.Message;
            }
        }
    }
}
