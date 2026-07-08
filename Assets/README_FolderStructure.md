# Zombie War — Folder Structure

Cấu trúc asset chính của game nằm trong `Assets/_Game/`.  
Folder `Assets/Scenes` mặc định của Unity được giữ nguyên; scene gameplay đặt ở `_Game/Scenes/`.

---

## Animations/

Animation clips & controllers theo nhân vật / vũ khí.

| Folder | Mục đích |
|--------|----------|
| `Player/` | Animation player (move, shoot, die…) |
| `Zombie/` | Animation zombie (idle, chase, attack…) |
| `Weapons/` | Animation vũ khí (fire, reload…) |

## Audio/

| Folder | Mục đích |
|--------|----------|
| `Music/` | Nhạc nền (BGM) |
| `SFX/` | Hiệu ứng âm thanh (súng, nổ, UI…) |

## Materials/

Material gắn cho mesh / VFX.

| Folder | Mục đích |
|--------|----------|
| `Ground/` | Nền / sàn map |
| `Player/` | Material player |
| `Zombie/` | Material zombie |
| `Weapons/` | Material vũ khí |
| `VFX/` | Material cho hiệu ứng |

## Models/

Model 3D (FBX/OBJ…).

| Folder | Mục đích |
|--------|----------|
| `Player/` | Model nhân vật người chơi |
| `Zombie/` | Model zombie |
| `Environment/` | Địa hình, props, map props |
| `Weapons/` | Model súng / vũ khí |

## Particles/

Particle System prefab / asset.

| Folder | Mục đích |
|--------|----------|
| `Gun/` | Muzzle flash, hit spark… |
| `Bomb/` | Hiệu ứng nổ bom |
| `Zombie/` | VFX liên quan zombie (máu, death…) |
| `Environment/` | Khói, bụi, ambient |

## Prefabs/

Prefab sẵn sàng kéo vào scene.

| Folder | Mục đích |
|--------|----------|
| `Player/` | Prefab người chơi |
| `Zombie/` | Prefab zombie |
| `Weapons/` | Prefab vũ khí |
| `Bullets/` | Prefab đạn |
| `Bombs/` | Prefab bom / lựu đạn |
| `UI/` | Prefab panel / widget UI |
| `VFX/` | Prefab hiệu ứng dùng lại |
| `Environment/` | Prefab môi trường |

## Scenes/

Scene gameplay / level của Zombie War (không trùng với `Assets/Scenes` mặc định).

## Scripts/

Mã C# theo hệ thống.

| Folder | Mục đích |
|--------|----------|
| `Core/` | Manager, bootstrap, singleton chung |
| `Player/` | Di chuyển, input, máu player |
| `Camera/` | Camera follow / shake |
| `UI/` | HUD, menu, binding UI |
| `Zombie/` | AI, spawn, combat zombie |
| `Weapon/` | Bắn, reload, damage vũ khí |
| `Bomb/` | Logic bom / nổ |
| `Level/` | Load level, win/lose, wave |
| `Audio/` | Play music / SFX |
| `VFX/` | Spawn & điều khiển VFX |

## ScriptableObjects/

Data asset cấu hình (không hard-code trong code).

| Folder | Mục đích |
|--------|----------|
| `Weapons/` | Stats súng (damage, firerate…) |
| `Zombies/` | Stats / loại zombie |
| `Levels/` | Cấu hình map, wave, mục tiêu |

## Shaders/

| Folder | Mục đích |
|--------|----------|
| `Zombie/` | Shader riêng cho zombie |
| `VFX/` | Shader hiệu ứng |

## Sprites/

| Folder | Mục đích |
|--------|----------|
| `UI/` | Sprite giao diện |
| `Icons/` | Icon item / vũ khí / skill |

## UI/

Tài nguyên & layout UI (khác với `Prefabs/UI` và `Scripts/UI`).

| Folder | Mục đích |
|--------|----------|
| `Canvases/` | Canvas / panel layout |
| `Fonts/` | Font chữ |
| `Images/` | Ảnh UI lớn (banner, background…) |

## ThirdParty/

Asset / plugin bên thứ 3 (giữ tách biệt khỏi code game).

---

## Ghi chú

- Mỗi folder rỗng có file `.gitkeep` để Git tracking được.
- Không đặt gameplay script bên ngoài `Scripts/` trừ khi có lý do rõ ràng.
- Import package third-party vào `ThirdParty/`, tránh làm lộn xộn root `Assets/`.
