// SPDX-License-Identifier: GPL-2.0-only
// Fixed waveforms adapted from kaiT2en/KaiT2en-Fedora t2_trackpad_actuator.c.
#include <ntddk.h>
#include <wdf.h>
#include <usb.h>
#include <usbdlib.h>
#include <wdfusb.h>
#include <initguid.h>
DEFINE_GUID(GUID_DEVINTERFACE_T2_HAPTIC,0xdbe6fbda,0x5109,0x42ea,0x94,0x97,0x88,0x1d,0x31,0xf0,0x97,0x20);
#define IOCTL_T2_CLICK CTL_CODE(FILE_DEVICE_UNKNOWN,0x800,METHOD_BUFFERED,FILE_WRITE_DATA)
typedef struct { WDFUSBDEVICE Usb; ULONGLONG LastClick; } T2_DEVICE_CONTEXT;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(T2_DEVICE_CONTEXT, GetContext);
DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD DeviceAdd;
EVT_WDF_DEVICE_PREPARE_HARDWARE Prepare;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL Control;

NTSTATUS DriverEntry(PDRIVER_OBJECT object,PUNICODE_STRING path) {
 WDF_DRIVER_CONFIG config;
 WDF_DRIVER_CONFIG_INIT(&config,DeviceAdd);
 return WdfDriverCreate(object,path,WDF_NO_OBJECT_ATTRIBUTES,&config,WDF_NO_HANDLE);
}
NTSTATUS DeviceAdd(WDFDRIVER driver,PWDFDEVICE_INIT init) {
 WDF_PNPPOWER_EVENT_CALLBACKS pnp; WDF_OBJECT_ATTRIBUTES attrs;
 WDF_IO_QUEUE_CONFIG queue; WDFDEVICE device; NTSTATUS status;
 UNREFERENCED_PARAMETER(driver);
 WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&pnp); pnp.EvtDevicePrepareHardware=Prepare;
 WdfDeviceInitSetPnpPowerEventCallbacks(init,&pnp);
 WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attrs,T2_DEVICE_CONTEXT);
 attrs.ExecutionLevel=WdfExecutionLevelPassive;
 status=WdfDeviceCreate(&init,&attrs,&device); if(!NT_SUCCESS(status)) return status;
 status=WdfDeviceCreateDeviceInterface(device,&GUID_DEVINTERFACE_T2_HAPTIC,NULL);
 if(!NT_SUCCESS(status)) return status;
 WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queue,WdfIoQueueDispatchSequential);
 queue.PowerManaged=WdfTrue; queue.EvtIoDeviceControl=Control;
 return WdfIoQueueCreate(device,&queue,WDF_NO_OBJECT_ATTRIBUTES,WDF_NO_HANDLE);
}
NTSTATUS Prepare(WDFDEVICE device,WDFCMRESLIST raw,WDFCMRESLIST translated) {
 T2_DEVICE_CONTEXT *ctx=GetContext(device); WDF_USB_DEVICE_CREATE_CONFIG config;
 WDF_USB_DEVICE_SELECT_CONFIG_PARAMS select; USB_DEVICE_DESCRIPTOR desc;
 NTSTATUS status;
 UNREFERENCED_PARAMETER(raw); UNREFERENCED_PARAMETER(translated);
 if(!ctx->Usb) {
  WDF_USB_DEVICE_CREATE_CONFIG_INIT(&config,USBD_CLIENT_CONTRACT_VERSION_602);
  status=WdfUsbTargetDeviceCreateWithParameters(device,&config,WDF_NO_OBJECT_ATTRIBUTES,&ctx->Usb);
  if(!NT_SUCCESS(status)) return status;
 }
 WdfUsbTargetDeviceGetDeviceDescriptor(ctx->Usb,&desc);
 if(desc.idVendor!=0x05ac || desc.idProduct!=0x0340) return STATUS_DEVICE_CONFIGURATION_ERROR;
 // This driver is intentionally bound to MI_03 only; never the composite parent.
 if(WdfUsbTargetDeviceGetNumInterfaces(ctx->Usb)!=1) return STATUS_DEVICE_CONFIGURATION_ERROR;
 if(WdfUsbInterfaceGetInterfaceNumber(WdfUsbTargetDeviceGetInterface(ctx->Usb,0))!=3)
  return STATUS_DEVICE_CONFIGURATION_ERROR;
 WDF_USB_DEVICE_SELECT_CONFIG_PARAMS_INIT_SINGLE_INTERFACE(&select);
 status=WdfUsbTargetDeviceSelectConfig(ctx->Usb,WDF_NO_OBJECT_ATTRIBUTES,&select);
 ctx->LastClick=0; return status;
}
VOID Control(WDFQUEUE queue,WDFREQUEST request,size_t outLen,size_t inLen,ULONG code) {
 T2_DEVICE_CONTEXT *ctx=GetContext(WdfIoQueueGetDevice(queue));
 ULONG *strength; NTSTATUS status; UCHAR report[14]; ULONG transferred=0;
 WDF_USB_CONTROL_SETUP_PACKET setup; WDF_MEMORY_DESCRIPTOR memory;
 WDF_REQUEST_SEND_OPTIONS options; ULONGLONG now;
 static const UCHAR light[14]={0x53,1,0x1c,0x8f,1,3,0,0x53,0x37,2,2,0,0x18,0x4d};
 static const UCHAR firm[14]={0x53,1,0x2d,0x8f,1,5,0,0x53,0x37,2,2,0,0x30,0x4d};
 UNREFERENCED_PARAMETER(outLen);
 if(code!=IOCTL_T2_CLICK) { WdfRequestComplete(request,STATUS_INVALID_DEVICE_REQUEST); return; }
 if(inLen!=sizeof(ULONG)) { WdfRequestComplete(request,STATUS_INVALID_BUFFER_SIZE); return; }
 status=WdfRequestRetrieveInputBuffer(request,sizeof(ULONG),(PVOID*)&strength,NULL);
 if(!NT_SUCCESS(status)) { WdfRequestComplete(request,status); return; }
 if(*strength>1 || !ctx->Usb) { WdfRequestComplete(request,STATUS_INVALID_PARAMETER); return; }
 now=KeQueryInterruptTime();
 if(ctx->LastClick && now-ctx->LastClick<1000000) {
  WdfRequestComplete(request,STATUS_DEVICE_BUSY); return;
 }
 ctx->LastClick=now;
 RtlCopyMemory(report,*strength?firm:light,sizeof(report));
 // HID class SET_REPORT(Output, 0x53), interface 3, exact 14-byte payload.
 WDF_USB_CONTROL_SETUP_PACKET_INIT(&setup,BmRequestHostToDevice,BmRequestToInterface,9,0x0253,3);
 setup.Packet.bm.Request.Type=BmRequestClass;
 WDF_MEMORY_DESCRIPTOR_INIT_BUFFER(&memory,report,sizeof(report));
 WDF_REQUEST_SEND_OPTIONS_INIT(&options,WDF_REQUEST_SEND_OPTION_TIMEOUT);
 WDF_REQUEST_SEND_OPTIONS_SET_TIMEOUT(&options,WDF_REL_TIMEOUT_IN_MS(500));
 status=WdfUsbTargetDeviceSendControlTransferSynchronously(ctx->Usb,WDF_NO_HANDLE,&options,&setup,&memory,&transferred);
 if(NT_SUCCESS(status) && transferred!=sizeof(report)) status=STATUS_DEVICE_DATA_ERROR;
 WdfRequestComplete(request,status);
}

