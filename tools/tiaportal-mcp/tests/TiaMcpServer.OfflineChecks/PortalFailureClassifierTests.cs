using System;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// PortalFailureClassifier 判断「这次调用失败」还是「TIA 进程已经没了」。判错的代价不对称：
    /// 把"进程没了"当普通失败，调用方会去重试一个不可能成功的调用，而真正该做的
    /// （重连、重做、重新存盘）一件都不会做 —— 所以正反两向都要钉。
    /// </summary>
    internal static class PortalFailureClassifierTests
    {
        // 用类型名里含关键字的假异常模拟 Openness 的 NonRecoverable / Remoting 异常
        // （真类型随 TIA 版本变；RemotingException 也不在 net8 里）。
        private sealed class NonRecoverableException : Exception
        {
            public NonRecoverableException(string m) : base(m) { }
        }

        private sealed class RemotingException : Exception
        {
            public RemotingException(string m) : base(m) { }
        }

        public static void Run()
        {
            T.Check("null -> not lost", !PortalFailureClassifier.IsPortalProcessLost(null));

            T.Check("NonRecoverable type name -> lost",
                    PortalFailureClassifier.IsPortalProcessLost(new NonRecoverableException("boom")));

            T.Check("NonRecoverable only in message -> lost",
                    PortalFailureClassifier.IsPortalProcessLost(
                        new Exception("Siemens.Engineering.NonRecoverableException: the process is gone")));

            T.Check("COMException -> lost",
                    PortalFailureClassifier.IsPortalProcessLost(
                        new System.Runtime.InteropServices.COMException("RPC server unavailable")));

            T.Check("RemotingException type name -> lost",
                    PortalFailureClassifier.IsPortalProcessLost(new RemotingException("channel dead")));

            T.Check("inner NonRecoverable -> lost",
                    PortalFailureClassifier.IsPortalProcessLost(
                        new Exception("wrapper", new NonRecoverableException("x"))));

            T.Check("case-insensitive message match",
                    PortalFailureClassifier.IsPortalProcessLost(new Exception("saw nonrecoverableEXCEPTION here")));

            // 反向：普通失败绝不能被误判成"进程没了"（否则会把可重试的错说成灾难）。
            T.Check("ordinary failure -> NOT lost",
                    !PortalFailureClassifier.IsPortalProcessLost(new InvalidOperationException("just a bad argument")));
        }
    }
}
