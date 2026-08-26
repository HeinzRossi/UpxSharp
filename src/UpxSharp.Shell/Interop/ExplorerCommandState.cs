using System;

namespace UpxSharp.Shell.Interop;

[Flags]
public enum ExplorerCommandState : uint
{
    ECS_ENABLED = 0x0000,
    ECS_DISABLED = 0x0001,
    ECS_HIDDEN = 0x0002,
    ECS_CHECKBOX = 0x0004,
    ECS_CHECKED = 0x0008,
    ECS_RADIOCHECK = 0x0010,
}
