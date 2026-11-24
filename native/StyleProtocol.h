#pragma once

#include <windows.h>
#include <array>
#include <cstdint>
#include <string>

inline constexpr size_t regionCount = 7;

enum class Material : uint32_t { Original, Solid, Transparent, Acrylic, Blur, Mica };

struct Style {
    Material material;
    uint32_t argb;
    uint32_t tint;
    uint32_t luminosity;
    uint32_t blur;
    uint32_t opacity;
    std::array<uint32_t, 3> palette;
    bool operator==(const Style&) const = default;
};

struct Session {
    volatile LONG ready;
    volatile LONG request;
    volatile LONG completed;
    volatile LONG matched;
    volatile LONG applied;
    volatile LONG failed;
    volatile LONG blocked;
    volatile LONG external;
    std::array<Style, regionCount> regions;
    volatile LONG samples;
};

static_assert(sizeof(Style) == 36 && sizeof(Session) == 288);

inline std::wstring SessionName(DWORD process)
{
    return L"Local\\kzlSystemCustomize.StartMenu.5." + std::to_wstring(process);
}

inline UINT ConnectionMessage()
{
    static const UINT message = RegisterWindowMessageW(L"kzlSystemCustomize.ConnectStartMenu.5");
    return message;
}
