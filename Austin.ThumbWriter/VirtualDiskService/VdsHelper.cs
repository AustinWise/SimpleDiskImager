using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VdsWrapper;

namespace Austin.ThumbWriter.VirtualDiskService
{
    //It is not ok to call into the VDS COM objects from a Window procedure function.
    //You get the RPC_E_CANTCALLOUT_ININPUTSYNCCALL error. Therefore all calls to VDS
    //are executed on a different thread.
    static class VdsHelper
    {
        const TaskCreationOptions TaskOptions = TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler | TaskCreationOptions.DenyChildAttach;

        public static async Task RunBlockingAction(Action action)
        {
            await RunBlockingAction<object>(() =>
            {
                action();
                return null;
            });
        }

        public static async Task<T> RunBlockingAction<T>(Func<T> action)
        {
            //TODO: Figure out if the thread really should be MTA.
            //Also, from a .NET perspective, the RunContinuationsAsynchronously is totally unneeded.
            //We know exactly what is happening on the thread when we call SetResult, so there is no deadlock.
            //However as a belt and suspenders measure, we queue the continuation to the ThreadPool
            //to prevent any COM MTA stuff from effecting callbacks.
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var t = new Thread(() =>
            {
                try
                {
                    tcs.SetResult(action());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            t.IsBackground = true;
            t.Name = "VDS Background Thread";
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            return await tcs.Task;
        }

        public delegate void EnumGetter(out IEnumVdsObject ppEnum);
        public static IEnumerable<T> VdsEnumerate<T>(EnumGetter enumGet)
        {
            IEnumVdsObject ppEnum;
            enumGet(out ppEnum);
            while (true)
            {
                uint fetched;
                object unknown;
                ppEnum.Next(1, out unknown, out fetched);

                if (fetched == 0 || unknown == null) break;

                yield return (T)unknown;
            }
        }

        public delegate void VsdAsyncAction(out IVdsAsync vsdAsync);

        public static async Task RunActionAsync(VsdAsyncAction asyncAction)
        {
            var task = RunBlockingAction<int>(() =>
            {
                IVdsAsync vdsAsync;
                asyncAction(out vdsAsync);
                int hr;
                _VDS_ASYNC_OUTPUT asyncOut;
                vdsAsync.Wait(out hr, out asyncOut);
                return hr;
            });
            Marshal.ThrowExceptionForHR(await task);
        }
    }
}
