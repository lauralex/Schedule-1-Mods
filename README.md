# Schedule 1 Backpack Mod

![License](https://img.shields.io/github/license/lauralex/Schedule-1-Mods)
![Language](https://img.shields.io/badge/Language-C%23-green)
![ModLoader](https://img.shields.io/badge/ModLoader-MelonLoader-red)

A sophisticated **MelonLoader** mod for *Schedule 1* that introduces a persistent, networked personal inventory system. This mod seamlessly integrates with the game's existing storage logic to provide every player with their own private "Backpack" storage, accessible anywhere via a hotkey.

## ✨ Features

*   **Personal Storage:** Every player gets a dedicated storage container (equivalent to a Medium Storage Rack).
*   **Instant Access:** Press **`B`** to toggle your backpack inventory anywhere in the world.
*   **Multiplayer Sync:** Fully networked using the game's native FishNet implementation. Items are synchronized in real-time between Host and Clients.
*   **Persistence:** Backpack contents are saved automatically with the world save. Items remain safe across server restarts and re-connections.
*   **Seamless Integration:** Uses the game's native UI and interaction logic—it feels like a built-in feature.

## 🛠️ Installation

1.  Download and install [MelonLoader](https://melonwiki.xyz/) (v0.6.0 or newer) for *Schedule 1*.
2.  Download the latest release of `Schedule1Backpack.dll` from the [Releases page](../../releases).
3.  Place the `.dll` file into your game's `Mods` directory (e.g., `Schedule I/Mods/`).
4.  Launch the game.

## 🕹️ Usage

*   **Open/Close Backpack:** Press **`B`** on your keyboard.
*   **Interaction:** Drag and drop items just like you would with any storage rack in the game.
*   **Host/Client:** Works for both the host and joining players. Each player has a unique inventory linked to their SteamID.

---

## 👨‍💻 Engineering & Development

This mod was developed through extensive reverse engineering of the game's IL2CPP structure. Because *Schedule 1* uses the IL2CPP scripting backend, standard C# reflection was insufficient.

### The "Underground" Architecture
To ensure compatibility with the game's save system without modifying game assets, this mod employs a "Shadow Grid" technique:
1.  **Harmony Patches:** We hook into the `Grid.Awake` method to inject logical "Tiles" at coordinates `(10000, 10000)`—far outside the playable map.
2.  **Invisible Entities:** We spawn standard `PlaceableStorageEntity` objects, strip them of their meshes and colliders, and force their position deep underground (`Y = -1500`).
3.  **SteamID Mapping:** The mod maps the local player's SteamID (via `Player.Local.PlayerCode`) to a specific hidden tile, ensuring that when you rejoin a server, you regain ownership of your specific box.

### 🧰 Tools of the Trade

Creating this mod required a full suite of reverse-engineering tools to analyze the game's memory layout, function pointers, and networking logic.

| Tool | Purpose |
| :--- | :--- |
| **[IL2CppInspectorRedux](https://github.com/LukeFZ/Il2CppInspectorRedux)** | The backbone of the analysis. Used to extract the scaffold of the game's code and generate **Python scripts for IDA**, allowing us to map function names and signatures onto the raw assembly code. |
| **[Cpp2IL](https://github.com/SamboyCoding/Cpp2IL)** | Essential for generating "best-effort" C# decompilation of the IL2CPP function bodies. This helped visualize the high-level logic flow of complex methods like `InitializeGridItem` and `OnPlayerConnectionUpdated`. |
| **[IDA Pro](https://hex-rays.com/ida-pro/)** | Used for static analysis of the game's `GameAssembly.dll`. IDA was critical for identifying a crash caused by a null-reference exception in the native C++ code within `GridItem::InitializeGridItem` when no valid Grid was passed. |
| **[UnityExplorer](https://github.com/yukieiji/UnityExplorer)** | An invaluable runtime inspection tool. Used to browse the scene hierarchy live, identify the structure of `Grid` and `Tile` objects, and test property values in real-time. |
| **[Cheat Engine](https://www.cheatengine.org/)** | Used for memory debugging and verifying that our coordinate offsets were not overlapping with valid game memory or physical entities. |

## 📄 License

This project is licensed under the Apache-2.0 License - see the [LICENSE](LICENSE) file for details.

---
*Disclaimer: This mod is a fan creation and is not affiliated with the developers of Schedule 1. Use at your own risk.*
