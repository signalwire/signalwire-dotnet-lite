using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SignalWire.Relay.Calling
{
    public sealed class RecordAction
    {
        internal Call Call { get; set; }

        public string ControlID { get; internal set; }

        public bool Completed { get; internal set; }

        public RecordResult Result { get; internal set; }

        public CallRecordState State { get; internal set; }

        public CallRecord Payload { get; internal set; }

        public string Url { get; internal set; }

        public StopResult Stop()
        {
            Task<LL_RecordStopResult> taskLLRecordStop = Call.API.LL_RecordStopAsync(new LL_RecordStopParams()
            {
                NodeID = Call.NodeID,
                CallID = Call.ID,
                ControlID = ControlID,
            });

            LL_RecordStopResult resultLLRecordStop = taskLLRecordStop.Result;

            return new StopResult()
            {
                Successful = resultLLRecordStop.Code == "200",
            };
        }

        public RecordPauseResult Pause(RecordPauseBehavior behavior = RecordPauseBehavior.skip)
        {
            Task<LL_RecordPauseResult> taskLLRecordPause = Call.API.LL_RecordPauseAsync(new LL_RecordPauseParams()
            {
                NodeID = Call.NodeID,
                CallID = Call.ID,
                ControlID = ControlID,
                Behavior = behavior,
            });

            LL_RecordPauseResult resultLLRecordPause = taskLLRecordPause.Result;

            return new RecordPauseResult()
            {
                Successful = resultLLRecordPause.Code == "200",
            };
        }

        public RecordResumeResult Resume()
        {
            Task<LL_RecordResumeResult> taskLLRecordResume = Call.API.LL_RecordResumeAsync(new LL_RecordResumeParams()
            {
                NodeID = Call.NodeID,
                CallID = Call.ID,
                ControlID = ControlID,
            });

            LL_RecordResumeResult resultLLRecordResume = taskLLRecordResume.Result;

            return new RecordResumeResult()
            {
                Successful = resultLLRecordResume.Code == "200",
            };
        }
    }
}
