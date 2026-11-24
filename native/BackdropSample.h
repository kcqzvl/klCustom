#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <winrt/Windows.UI.Core.h>

struct CoreWindowHandle : IUnknown
{
    virtual HRESULT STDMETHODCALLTYPE get_WindowHandle(HWND*) = 0;
};

template<> inline constexpr winrt::guid winrt::impl::guid_v<CoreWindowHandle>{
    0x45d64a29, 0xa63e, 0x4cb6, {0xb4, 0x98, 0x57, 0x81, 0xd2, 0x98, 0xcb, 0x4f}};

namespace backdrop
{
    inline constexpr size_t stopCount = 64;
    using Colors = std::array<winrt::Windows::UI::Color, stopCount>;

    inline bool Sample(RECT area, Colors& colors)
    {
        int width = area.right - area.left;
        int height = area.bottom - area.top;
        if (width < 1 || width > 64 || height < 1 || height > 8192)
            return false;

        HDC screen = GetDC(nullptr);
        if (!screen)
            return false;
        HDC buffer = CreateCompatibleDC(screen);
        BITMAPINFO info{};
        info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = width;
        info.bmiHeader.biHeight = -height;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = BI_RGB;
        void* pixels{};
        HBITMAP bitmap = buffer ? CreateDIBSection(buffer, &info, DIB_RGB_COLORS, &pixels, nullptr, 0) : nullptr;
        HGDIOBJ previous = bitmap ? SelectObject(buffer, bitmap) : nullptr;
        bool success = previous && previous != HGDI_ERROR
            && BitBlt(buffer, 0, 0, width, height, screen, area.left, area.top, SRCCOPY | CAPTUREBLT);
        if (success)
        {
            GdiFlush();
            auto image = static_cast<const uint32_t*>(pixels);
            for (size_t i = 0; i < colors.size(); i++)
            {
                int y = static_cast<int>(i * (height - 1) / (colors.size() - 1));
                auto center = image + y * width;
                std::array<uint32_t, 64> strip{};
                std::copy_n(center, width, strip.begin());
                auto luminance = [](uint32_t color) {
                    return ((color >> 16) & 255) * 54 + ((color >> 8) & 255) * 183 + (color & 255) * 19;
                };
                auto middle = strip.begin() + width / 2;
                std::nth_element(strip.begin(), middle, strip.begin() + width, [&](uint32_t left, uint32_t right) {
                    return luminance(left) < luminance(right);
                });
                colors[i] = {255, static_cast<uint8_t>(*middle >> 16), static_cast<uint8_t>(*middle >> 8),
                    static_cast<uint8_t>(*middle)};
            }
        }
        if (previous && previous != HGDI_ERROR) SelectObject(buffer, previous);
        if (bitmap) DeleteObject(bitmap);
        if (buffer) DeleteDC(buffer);
        ReleaseDC(nullptr, screen);
        return success;
    }

    inline bool MainBackgroundArea(xaml::FrameworkElement const& source, RECT& area)
    {
        auto root = source.XamlRoot();
        auto core = winrt::Windows::UI::Core::CoreWindow::GetForCurrentThread();
        if (!root || !root.IsHostVisible() || !core || !core.Visible()
            || source.ActualWidth() < 80 || source.ActualHeight() < 80)
            return false;
        HWND hwnd{};
        auto interop = core.try_as<CoreWindowHandle>();
        if (!interop || FAILED(interop->get_WindowHandle(&hwnd)) || !hwnd)
            return false;
        RECT client{};
        POINT origin{};
        if (!GetClientRect(hwnd, &client) || !ClientToScreen(hwnd, &origin))
            return false;

        auto position = source.TransformToVisual(nullptr).TransformPoint({0, 0});
        double scale = root.RasterizationScale();
        LONG left = std::lround((position.X + source.ActualWidth() - 24) * scale);
        LONG top = std::lround((position.Y + 6) * scale);
        LONG right = std::lround((position.X + source.ActualWidth() - 12) * scale);
        LONG bottom = std::lround((position.Y + source.ActualHeight() - 6) * scale);
        if (left < 0 || top < 0 || right > client.right || bottom > client.bottom)
            return false;
        area = {origin.x + left, origin.y + top, origin.x + right, origin.y + bottom};
        return true;
    }

    inline bool HasPopup(xaml::FrameworkElement const& source)
    {
        auto root = source.XamlRoot();
        if (!root)
            return false;
        return xaml::Media::VisualTreeHelper::GetOpenPopupsForXamlRoot(root).Size() != 0;
    }
}
