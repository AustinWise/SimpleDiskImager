using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VdsWrapper;

namespace Austin.ThumbWriter.VirtualDiskService
{
    public sealed class VdsSession : IDisposable
    {
        const uint FILE_DEVICE_DISK = 0x00000007;
        const uint MEDIA_TYPE_RemovableMedia = 11;

        public static async Task<VdsSession> CreateAsync()
        {
            var task = VdsHelper.RunBlockingAction(() =>
            {
                var loader = new VdsServiceLoader();
                IVdsService service;
                loader.LoadService(".", out service);

                service.WaitForServiceReady();

                return service;
            });
            return new VdsSession(await task);
        }

        private readonly IVdsService mService;
        private bool mDisposed;

        private VdsSession(IVdsService service)
        {
            mService = service;
        }

        public async Task<List<Disk>> GetDisksAsync()
        {
            if (mDisposed)
                throw new ObjectDisposedException(nameof(VdsSession));

            return await VdsHelper.RunBlockingAction(() =>
            {
                var ret = new List<Disk>();

                foreach (var swProvider in VdsHelper.VdsEnumerate<IVdsSwProvider>((out IEnumVdsObject ppEnum) =>
                    mService.QueryProviders((uint)_VDS_QUERY_PROVIDER_FLAG.VDS_QUERY_SOFTWARE_PROVIDERS, out ppEnum)))
                {
                    foreach (var pack in VdsHelper.VdsEnumerate<IVdsPack>(swProvider.QueryPacks))
                    {
                        foreach (var disk in VdsHelper.VdsEnumerate<IVdsDisk>(pack.QueryDisks))
                        {
                            _VDS_DISK_PROP diskProps;
                            disk.GetProperties(out diskProps);

                            if (diskProps.dwDeviceType != FILE_DEVICE_DISK)
                                continue;
                            if (diskProps.status != _VDS_DISK_STATUS.VDS_DS_ONLINE)
                            {
                                //The disk might be ejected. Or a card reader might have no media in it.
                                continue;
                            }
                            if (diskProps.health != _VDS_HEALTH.VDS_H_HEALTHY)
                            {
                                //TODO: does it ever make sense to try to write a non-healthy drive?
                                continue;
                            }

                            //Other properties that might be usful to expose:
                            //* busType. For example, USB versus MMC
                            ret.Add(new Disk(disk, diskProps.id, diskProps.pwszName, diskProps.pwszFriendlyName, diskProps.dwMediaType == MEDIA_TYPE_RemovableMedia, checked((long)diskProps.ullSize)));
                        }
                    }
                }

                ret.Sort();

                return ret;
            });
        }

        public void Dispose()
        {
            if (mDisposed)
                throw new ObjectDisposedException(nameof(VdsSession));
            mDisposed = true;
        }
    }
}
