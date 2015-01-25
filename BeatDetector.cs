using System;
using System.Threading;
using Un4seen.Bass;
using Un4seen.BassWasapi;

namespace Szab.BeatDetector
{
    public enum SensivityLevel
    {
        VERY_LOW = 120,
        LOW = 110,
        NORMAL = 100,
        HIGH = 80,
        VERY_HIGH = 70
    };

    public sealed class SpectrumBeatDetector
    {
        #region Fields

        // Constants
        private const int BANDS = 10;
        private const int HISTORY = 20;

        // Events
        public delegate void BeatDetectedHandler(byte Value);
        private event BeatDetectedHandler OnDetected;

        // Threading
        private Thread _AnalysisThread;

        // BASS Process
        private WASAPIPROC _WasapiProcess = new WASAPIPROC(SpectrumBeatDetector.Process);

        // Analysis settings
        private int _SamplingRate;
        private int _DeviceCode;
        private SensivityLevel _BASSSensivity;
        private SensivityLevel _MIDSSensivity;

        // Analysis data
        private float[] _FFTData = new float[4096];
        private double[,] _History = new double[BANDS, HISTORY];

        #endregion

        #region Setup methods

        public SpectrumBeatDetector(int DeviceCode, int SamplingRate = 44100, SensivityLevel BASSSensivity = SensivityLevel.NORMAL, SensivityLevel MIDSSensivity = SensivityLevel.NORMAL)
        {
            _SamplingRate = SamplingRate;
            _BASSSensivity = BASSSensivity;
            _MIDSSensivity = MIDSSensivity;
            _DeviceCode = DeviceCode;
            Init();
        }

        // BASS initialization method
        private void Init()
        {
            bool result = false;

            // Initialize BASS on default device
            result = Bass.BASS_Init(0, _SamplingRate, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero);

            if (!result)
            {
                throw new BassInitException(Bass.BASS_ErrorGetCode().ToString());
            }

            // Initialize WASAPI
            result = BassWasapi.BASS_WASAPI_Init(_DeviceCode, 0, 0, BASSWASAPIInit.BASS_WASAPI_BUFFER, 1f, 0.05f, _WasapiProcess, IntPtr.Zero);

            if (!result)
            {
                throw new BassWasapiInitException(Bass.BASS_ErrorGetCode().ToString());
            }

            BassWasapi.BASS_WASAPI_Start();
            System.Threading.Thread.Sleep(500);
        }


        ~SpectrumBeatDetector()
        {
            // Kill working thread and clean after BASS
            if(_AnalysisThread != null && _AnalysisThread.IsAlive)
            {
                _AnalysisThread.Abort();
            }

            Free();
        }

        // Sensivity Setters
        public void SetBassSensivity(SensivityLevel Sensivity)
        {
            _BASSSensivity = Sensivity;
        }

        public void SetMidsSensivity(SensivityLevel Sensivity)
        {
            _MIDSSensivity = Sensivity;
        }

        #endregion

        #region BASS-dedicated Methods

        // WASAPI callback, required for continuous recording
        private static int Process(IntPtr buffer, int length, IntPtr user)
        {
            return length;
        }

        // Cleans after BASS
        public void Free()
        {
            BassWasapi.BASS_WASAPI_Free();
            Bass.BASS_Free();
        }

        #endregion

        #region Analysis public methods

        // Starts a new Analysis Thread
        public void StartAnalysis()
        {
            // Kills currently running analysis thread if alive
            if (_AnalysisThread != null && _AnalysisThread.IsAlive)
            {
                _AnalysisThread.Abort();
            }

            // Starts a new high-priority thread
            _AnalysisThread = new Thread(delegate()
                {
                    while (true)
                    {
                        //Stopwatch SW = new Stopwatch();
                        //SW.Start();
                        Thread.Sleep(4);
                        PerformAnalysis();
                        //SW.Stop();
                        //Console.WriteLine(SW.Elapsed);
                    }
                });

            _AnalysisThread.Priority = ThreadPriority.Highest;
            _AnalysisThread.Start();
        }

        // Kills running thread
        public void StopAnalysis()
        {
            if(_AnalysisThread != null && _AnalysisThread.IsAlive)
            {
                _AnalysisThread.Abort();
            }
        }

        #endregion

        #region Event handling

        public void Subscribe(BeatDetectedHandler Delegate)
        {
            OnDetected += Delegate;
        }

        public void UnSubscribe(BeatDetectedHandler Delegate)
        {
            OnDetected -= Delegate;
        }

        #endregion

        #region Analysis private methods

        // Shifts history n places to the right
        private void ShiftHistory(int n)
        {
            for (int i = 0; i < BANDS; i++)
            {
                for (int j = HISTORY - 1 - n; j >= 0; j--)
                {
                    _History[i, j + n] = _History[i, j];
                }
            }
        }

        // Performs FFT analysis in order to detect beat
        private void PerformAnalysis()
        {
            // Specifes on which result end which band (dividing it into 10 bands)
            // 19 - bass, 187 - mids, rest is highs
            int[] BandRange = { 4, 8, 18, 38, 48, 94, 140, 186, 466, 1022, 22000};
            double[] BandsTemp = new double[BANDS];
            int n = 0;

            // Get FFT
            int ret = BassWasapi.BASS_WASAPI_GetData(_FFTData, (int)BASSData.BASS_DATA_FFT1024 | (int)BASSData.BASS_DATA_FFT_COMPLEX); //get channel fft data
            if (ret < -1) return;

            // Calculate the energy of every result and divide it into subbands
            float sum = 0;

            for (int i = 2; i < 2048; i = i + 2)
            {
                float real = _FFTData[i];
                float complex = _FFTData[i + 1];
                sum += (float)Math.Sqrt((double)(real * real + complex * complex));

                if(i == BandRange[n])
                {
                    BandsTemp[n++] = (BANDS * sum) / 1024;
                    sum = 0;
                }
            }

            // Detect beat basing on FFT results
            DetectBeat(BandsTemp);

            // Shift the history register and save new values
            ShiftHistory(1);

            for (int i = 0; i < BANDS; i++)
            {
                _History[i, 0] = BandsTemp[i];
            }
        }

        // Calculate the average value of every band
        private double[] CalculateAverages()
        {
            double[] avg = new double[BANDS];

            for (int i = 0; i < BANDS; i++)
            {
                double sum = 0;

                for (int j = 0; j < HISTORY; j++)
                {
                    sum += _History[i, j];
                }

                avg[i] = (sum / HISTORY);
            }

            return avg;
        }

        // Detects beat basing on analysis result
        // Beat detection is marked on the first three bits of the returned value
        private byte DetectBeat(double[] Energies)
        {
            // Sound height ranges (first 2 is bass, next 6 is mids)
            int Bass = 2;
            int Mids = 8;

            double[] avg = CalculateAverages();
            byte result = 0;

            for (int i = 0; i < BANDS && result == 0; i++)
            {
                // Set the C parameter
                double C = 0;

                if (i < Bass)
                {
                    C = 2.4 * ((double)_BASSSensivity / 100);
                }
                else if (i < Mids)
                {
                    C = 3 * ((double)_MIDSSensivity / 100);
                }
                else
                {
                    C = 2.3;
                }

                // Compare energies in all bands with C*average
                if(Energies[i] > (C * avg[i]))
                {
                    byte res = 0;
                    if(i<Bass)
                    {
                        res = 1;
                    }
                    else if (i < Mids)
                    {
                        res = 2;
                    }
                    else
                    {
                        res = 4;
                    }
                    result = (byte)(result | res);
                }
            }

            if(result > 0)
            {
                OnDetected(result);
            }

            return result;
        }

        #endregion

    }
}
