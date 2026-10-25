# Installing MDraftingHeper

MDraftingHeper adds a **MDraftingHeper** tab to the AutoCAD Ribbon with three tools: Rebar Percentage, Tie & Hook, and Steel Section.

## Requirements

- Windows 10 or 11, 64-bit
- **AutoCAD 2021** (full version, not AutoCAD LT). Other AutoCAD versions will not load it.
- No administrator rights are needed. It installs for your Windows user only.

## Install

1. **Close AutoCAD** completely.
2. If you received a zip file, **extract all of it** to a folder (right-click the zip → *Extract All…*). Do not run the installer from inside the zip.
3. Double-click **`Install.cmd`** in the extracted folder.
   - If Windows shows "Windows protected your PC", click *More info* → *Run anyway*.
   - The window should end with **Installation complete.** Press any key to close it.
4. Start AutoCAD 2021. The **MDraftingHeper** tab appears on the Ribbon, with *Rebar Percentage*, *Tie & Hook* and *Steel Section*.

If AutoCAD shows a security message about loading MDraftingHeper, choose **Always Load**.

## Check that it works

- Type `MDH_STEEL` at the AutoCAD command line. The Steel Section dialog should open.
- Insert a section, select it and press **Ctrl+1**. The **Custom** group in the Properties palette lists its dimensions (for example H, B, tw, tf, r). Change a value and the shape updates.

## Update to a newer version

Close AutoCAD, extract the new zip, and double-click `Install.cmd` again. It replaces the previous version.

## Uninstall

Close AutoCAD and double-click **`Uninstall.cmd`**. Your drawings are not affected; sections already inserted stay editable in Ctrl+1 even without MDraftingHeper.

## Troubleshooting

| Problem | What to do |
| --- | --- |
| "AutoCAD is running" | Close every AutoCAD window (check the taskbar), then run `Install.cmd` again. |
| "MDraftingHeper.bundle not found" | You ran the installer without extracting the whole zip. Extract all files, then run `Install.cmd` from that folder. |
| No MDraftingHeper tab after starting AutoCAD | Make sure it is AutoCAD **2021**. Type `MDH_REBAR`: if the command is unknown, run `Install.cmd` again. If the command works but the tab is missing, switch workspace once (gear icon at the bottom right). |
| Tab appears, but a tool reports an error | The message includes the location of a log file (`%LOCALAPPDATA%\MDraftingHeper\logs\mdh.log`). Send that file to whoever gave you MDraftingHeper. |

## Good to know

- The steel profiles (UB/UC, IPE/HE A/B/M, CHS/SHS/RHS hot finished and cold formed) come from open datasets, not a manufacturer catalogue. Check critical sizes against your supplier's data. Hot-finished SHS/RHS are drawn with inner corner radius r − t (0.5 t), smaller than the 1.0 t of EN 10210-2.
- The "EC2 + Singapore NA" hook preset is **unverified**. Use *Manual input* unless the values have been checked.
- Installed files: `%APPDATA%\Autodesk\ApplicationPlugins\MDraftingHeper.bundle`. Settings and logs: `%APPDATA%\MDraftingHeper` and `%LOCALAPPDATA%\MDraftingHeper`.
