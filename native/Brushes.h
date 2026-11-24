#pragma once

#include "StyleProtocol.h"
#include <algorithm>
#include <unknwn.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Graphics.Effects.h>
#include <winrt/Windows.UI.Composition.h>
#include <winrt/Windows.UI.Xaml.Hosting.h>
#include <winrt/Windows.UI.Xaml.Media.h>

namespace xaml = winrt::Windows::UI::Xaml;

namespace ui = winrt::Windows::UI;
namespace media = ui::Xaml::Media;
namespace composition = ui::Composition;
namespace effects = winrt::Windows::Graphics::Effects;
namespace foundation = winrt::Windows::Foundation;

inline constexpr GUID gaussianBlurEffect{0x1feb6d69, 0x2fe6, 0x4ac9, {0x8c, 0x58, 0x1d, 0x7f, 0x93, 0xe7, 0xa6, 0xa5}};
inline constexpr GUID compositeEffect{0x48fc9f51, 0xf6ac, 0x48f1, {0x8b, 0x58, 0x3b, 0x28, 0xac, 0x46, 0xf7, 0x6d}};

// ABI declared by the Windows SDK's windows.graphics.effects.interop.h.
struct GraphicsEffectInterop : IUnknown
{
    virtual HRESULT STDMETHODCALLTYPE GetEffectId(GUID*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetNamedPropertyMapping(LPCWSTR, UINT*, int32_t*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetPropertyCount(UINT*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetProperty(UINT, winrt::impl::abi_t<foundation::IPropertyValue>**) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetSource(UINT, winrt::impl::abi_t<effects::IGraphicsEffectSource>**) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetSourceCount(UINT*) = 0;
};

template<> inline constexpr winrt::guid winrt::impl::guid_v<GraphicsEffectInterop>{
    0x2fc57384, 0xa068, 0x44d7, {0xa3, 0x31, 0x30, 0x98, 0x2f, 0xcf, 0x71, 0x77}};

inline ui::Color ColorFrom(uint32_t value)
{
    return {static_cast<uint8_t>(value >> 24), static_cast<uint8_t>(value >> 16),
        static_cast<uint8_t>(value >> 8), static_cast<uint8_t>(value)};
}

// Direct2D effect descriptions consumed by Windows Composition.
struct CompositionEffect : winrt::implements<CompositionEffect, effects::IGraphicsEffect,
    effects::IGraphicsEffectSource, GraphicsEffectInterop>
{
    explicit CompositionEffect(float radius)
        : kind(gaussianBlurEffect), radius(radius)
    {
        sources[0] = composition::CompositionEffectSourceParameter(L"Backdrop");
        sourceCount = 1;
    }

    explicit CompositionEffect(effects::IGraphicsEffectSource const& backdrop)
        : kind(compositeEffect)
    {
        sources[0] = backdrop;
        sources[1] = composition::CompositionEffectSourceParameter(L"Tint");
        sourceCount = 2;
    }

    winrt::hstring Name() const { return name; }
    void Name(winrt::hstring const& value) { name = value; }

    HRESULT STDMETHODCALLTYPE GetEffectId(GUID* id) noexcept override
    {
        if (!id) return E_POINTER;
        *id = kind;
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE GetNamedPropertyMapping(LPCWSTR, UINT*, int32_t*) noexcept override
    {
        return E_INVALIDARG;
    }

    HRESULT STDMETHODCALLTYPE GetPropertyCount(UINT* count) noexcept override
    {
        if (!count) return E_POINTER;
        *count = sourceCount == 1 ? 3 : 1;
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE GetProperty(UINT index, winrt::impl::abi_t<foundation::IPropertyValue>** value) noexcept override
    {
        if (!value) return E_POINTER;
        *value = nullptr;
        try
        {
            foundation::IInspectable property{nullptr};
            if (sourceCount == 2 && index == 0)
                property = foundation::PropertyValue::CreateUInt32(0); // Source over
            else if (sourceCount == 1 && index <= 2)
                property = index == 0 ? foundation::PropertyValue::CreateSingle(radius)
                    : foundation::PropertyValue::CreateUInt32(1); // Balanced optimization or hard border
            else
                return E_INVALIDARG;
            *value = reinterpret_cast<winrt::impl::abi_t<foundation::IPropertyValue>*>(
                winrt::detach_abi(property.as<foundation::IPropertyValue>()));
            return S_OK;
        }
        catch (...) { return winrt::to_hresult(); }
    }

    HRESULT STDMETHODCALLTYPE GetSource(UINT index, winrt::impl::abi_t<effects::IGraphicsEffectSource>** value) noexcept override
    {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (index >= sourceCount) return E_INVALIDARG;
        auto source = sources[index];
        *value = reinterpret_cast<winrt::impl::abi_t<effects::IGraphicsEffectSource>*>(winrt::detach_abi(source));
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE GetSourceCount(UINT* count) noexcept override
    {
        if (!count) return E_POINTER;
        *count = sourceCount;
        return S_OK;
    }

private:
    GUID kind;
    float radius{};
    UINT sourceCount{};
    winrt::hstring name;
    std::array<effects::IGraphicsEffectSource, 2> sources{nullptr, nullptr};
};

struct BlurBrush : media::XamlCompositionBrushBaseT<BlurBrush>
{
    explicit BlurBrush(composition::Compositor const& compositor, float radius, ui::Color tint)
        : compositor(compositor), tint(tint)
    {
        auto gaussian = winrt::make_self<CompositionEffect>(radius);
        auto graph = winrt::make_self<CompositionEffect>(gaussian.as<effects::IGraphicsEffectSource>());
        factory = compositor.CreateEffectFactory(graph.as<effects::IGraphicsEffect>());
    }

    void OnConnected()
    {
        if (CompositionBrush())
            return;
        auto brush = factory.CreateBrush();
        brush.SetSourceParameter(L"Backdrop", compositor.CreateBackdropBrush());
        brush.SetSourceParameter(L"Tint", compositor.CreateColorBrush(tint));
        CompositionBrush(brush);
    }

    void OnDisconnected() { CompositionBrush(nullptr); }

private:
    composition::Compositor compositor{nullptr};
    composition::CompositionEffectFactory factory{nullptr};
    ui::Color tint;
};

inline media::Brush MakeBrush(const Style& style, xaml::FrameworkElement const& owner)
{
    auto color = ColorFrom(style.argb);
    switch (style.material)
    {
    case Material::Solid: return media::SolidColorBrush(color);
    case Material::Transparent: return media::SolidColorBrush({0, 0, 0, 0});
    case Material::Acrylic:
    {
        media::AcrylicBrush brush;
        brush.BackgroundSource(media::AcrylicBackgroundSource::Backdrop);
        brush.TintColor(color);
        brush.TintOpacity(style.tint / 100.0);
        brush.TintLuminosityOpacity(winrt::box_value(style.luminosity / 100.0)
            .as<foundation::IReference<double>>());
        brush.FallbackColor({255, color.R, color.G, color.B});
        return brush;
    }
    case Material::Blur:
    {
        color.A = static_cast<uint8_t>((color.A * style.tint + 50) / 100);
        auto compositor = ui::Xaml::Hosting::ElementCompositionPreview::GetElementVisual(owner).Compositor();
        return winrt::make<BlurBrush>(compositor, static_cast<float>(style.blur), color).as<media::Brush>();
    }
    case Material::Mica:
    {
        media::LinearGradientBrush brush;
        brush.StartPoint({0, 0});
        brush.EndPoint({1, 1});
        brush.Opacity(style.opacity / 100.0);
        for (uint32_t i = 0; i < style.palette.size(); i++)
        {
            media::GradientStop stop;
            stop.Offset(i * 0.5);
            stop.Color(ColorFrom(style.palette[i]));
            brush.GradientStops().Append(stop);
        }
        return brush;
    }
    default: return nullptr;
    }
}
