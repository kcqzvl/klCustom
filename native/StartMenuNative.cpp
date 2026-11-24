#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <unknwn.h>
#undef GetCurrentTime
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.Core.h>
#include <chrono>
#include <utility>
#include <vector>
#include "StyleProtocol.h"
#include "RegionTargets.h"
#include "Brushes.h"
#include "BackdropSample.h"

namespace
{
    struct Tracked
    {
        winrt::weak_ref<xaml::FrameworkElement> element;
        Style style;
        int region;
        foundation::IInspectable original{nullptr};
        foundation::IInspectable current{nullptr};
    };

    class Backgrounds
    {
    public:
        Backgrounds(HANDLE mapping, Session* view) : mapping(mapping), session(view)
        {
            timer.Interval(std::chrono::milliseconds(400));
            timer.Tick([this](auto&&, auto&&) { Update(); });
        }

        ~Backgrounds()
        {
            timer.Stop();
            UnmapViewOfFile(session);
            CloseHandle(mapping);
        }

        void Start()
        {
            Update();
            timer.Start();
        }

    private:
        static bool Restore(xaml::FrameworkElement const& element, xaml::DependencyProperty const& property,
            Tracked const& item)
        {
            if (element.ReadLocalValue(property) != item.current)
                return false;
            if (item.original == xaml::DependencyProperty::UnsetValue())
                element.ClearValue(property);
            else
                element.SetValue(property, item.original);
            return true;
        }

        bool Apply(xaml::FrameworkElement const& element, TargetHit target, Style settings,
            media::LinearGradientBrush const& sharedBrush)
        {
            auto existing = std::find_if(tracked.begin(), tracked.end(),
                [&](auto const& item) { return item.element.get() == element; });
            auto property = BackgroundProperty(element);
            if (!property)
                return false;
            if (settings.material == Material::Original)
            {
                if (existing != tracked.end())
                {
                    Restore(element, property, *existing);
                    tracked.erase(existing);
                }
                return settings.material == Material::Original;
            }

            bool shared = target.region == 6 && !target.overlay && settings.material == Material::Acrylic
                && settings.palette[0] != 0 && sharedBrush;
            if (existing != tracked.end())
            {
                // The current writer owns this property until the user restores the region.
                if (existing->style == settings && (!shared || existing->current == sharedBrush))
                    return true;
            }
            else if (element.GetBindingExpression(property))
            {
                return false;
            }

            if (settings.tint > 100 || settings.luminosity > 100 || settings.blur > 64 || settings.opacity > 100)
                return false;
            auto brushStyle = settings;
            if (target.overlay)
                brushStyle.material = Material::Transparent;
            if (target.region != 6)
                brushStyle.palette[0] = 0;
            auto brush = shared ? sharedBrush.as<media::Brush>() : MakeBrush(brushStyle, element);
            if (!brush)
                return false;
            if (existing == tracked.end())
            {
                tracked.push_back({winrt::make_weak(element), settings, target.region, element.ReadLocalValue(property), brush});
                try { element.SetValue(property, brush); }
                catch (...) { tracked.pop_back(); throw; }
            }
            else
            {
                element.SetValue(property, brush);
                existing->style = settings;
                existing->current = brush;
            }
            return true;
        }

        void Visit(xaml::DependencyObject const& node, std::array<Style, regionCount> const& styles,
            media::LinearGradientBrush const& sharedBrush, LONG blocked, LONG& matched, LONG& applied,
            LONG& failed, int depth = 0)
        {
            if (depth > 100)
                return;
            if (auto element = node.try_as<xaml::FrameworkElement>())
            {
                auto target = FindTarget(element);
                if (target.region >= 0 && (blocked & (1L << target.region)) == 0)
                {
                    LONG bit = 1L << target.region;
                    if (!target.overlay)
                        matched |= bit;
                    auto style = styles[target.region];
                    try
                    {
                        if (!Apply(element, target, style, sharedBrush))
                            failed |= bit;
                        else if (!target.overlay && style.material != Material::Original)
                            applied |= bit;
                    }
                    catch (...) { failed |= bit; }
                }
            }
            for (int i = 0, n = media::VisualTreeHelper::GetChildrenCount(node); i < n; ++i)
                Visit(media::VisualTreeHelper::GetChild(node, i), styles, sharedBrush, blocked, matched, applied,
                    failed, depth + 1);
        }

        static xaml::FrameworkElement FindMainBackground(xaml::DependencyObject const& node, int depth = 0)
        {
            if (depth > 100)
                return nullptr;
            if (auto element = node.try_as<controls::Border>(); element && element.Name() == L"AcrylicBorder")
            {
                if (Named(Parent(element), L"MainMenu"))
                    return element;
            }
            for (int i = 0, count = media::VisualTreeHelper::GetChildrenCount(node); i < count; ++i)
                if (auto found = FindMainBackground(media::VisualTreeHelper::GetChild(node, i), depth + 1))
                    return found;
            return nullptr;
        }

        void RefreshBackdrop(xaml::DependencyObject const& root)
        {
            auto source = FindMainBackground(root);
            backdrop::Colors colors{};
            if (!source || !backdrop::SampleMainBackground(source, colors))
                return;
            if (!sharedBackground)
            {
                sharedBackground = media::LinearGradientBrush();
                sharedBackground.StartPoint({0, 0});
                sharedBackground.EndPoint({0, 1});
                for (size_t i = 0; i < colors.size(); ++i)
                {
                    media::GradientStop stop;
                    stop.Offset(static_cast<double>(i) / (colors.size() - 1));
                    stop.Color(colors[i]);
                    sharedBackground.GradientStops().Append(stop);
                }
            }
            else
            {
                auto stops = sharedBackground.GradientStops();
                for (uint32_t i = 0; i < colors.size(); ++i)
                    if (stops.GetAt(i).Color() != colors[i])
                        stops.GetAt(i).Color(colors[i]);
            }
            InterlockedIncrement(&session->samples);
        }

        void Update() noexcept
        {
            LONG revision = InterlockedCompareExchange(&session->request, 0, 0);
            if (revision <= 0 || (revision == lastRevision && idle))
                return;
            LONG matched{}, applied{}, failed{};
            try
            {
                auto styles = session->regions;
                if (revision != InterlockedCompareExchange(&session->request, 0, 0))
                    return;
                LONG blocked = InterlockedCompareExchange(&session->blocked, 0, 0);
                if (GetModuleHandleW(L"MToolExtend.dll"))
                    blocked |= 1;
                for (auto const& item : tracked)
                {
                    if (auto element = item.element.get())
                    {
                        auto property = BackgroundProperty(element);
                        if (element.ReadLocalValue(property) != item.current
                            && styles[item.region].material != Material::Original)
                            external |= 1L << item.region;
                    }
                }
                blocked |= external;
                std::erase_if(tracked, [](auto const& item) { return !item.element.get(); });
                for (auto entry = tracked.begin(); entry != tracked.end();)
                {
                    auto element = entry->element.get();
                    auto target = element ? FindTarget(element) : TargetHit{};
                    if (target.region < 0 || (blocked & (1L << entry->region)) != 0
                        || styles[entry->region].material == Material::Original)
                    {
                        if (element)
                            Restore(element, BackgroundProperty(element), *entry);
                        entry = tracked.erase(entry);
                    }
                    else
                        ++entry;
                }
                auto window = xaml::Window::Current();
                if (window)
                {
                    if (auto root = window.Content())
                    {
                        auto companion = styles[6];
                        bool shared = companion.material == Material::Acrylic && companion.palette[0] != 0;
                        if (shared && (blocked & (1L << 6)) == 0)
                            RefreshBackdrop(root);
                        else
                        {
                            sharedBackground = nullptr;
                            InterlockedExchange(&session->samples, 0);
                        }
                        Visit(root, styles, sharedBackground, blocked, matched, applied, failed);
                    }
                }
                idle = std::none_of(styles.begin(), styles.end(),
                    [](auto const& style) { return style.material != Material::Original; });
            }
            catch (...) { failed = -2; }
            InterlockedExchange(&session->matched, matched);
            InterlockedExchange(&session->applied, applied);
            InterlockedExchange(&session->failed, failed);
            InterlockedExchange(&session->external, external);
            InterlockedExchange(&session->completed, revision);
            lastRevision = revision;
        }

        HANDLE mapping;
        Session* session;
        xaml::DispatcherTimer timer;
        std::vector<Tracked> tracked;
        media::LinearGradientBrush sharedBackground{nullptr};
        LONG lastRevision{};
        LONG external{};
        bool idle{};
    };

    bool IsStartMenuHost()
    {
        wchar_t actual[MAX_PATH]{};
        wchar_t windows[MAX_PATH]{};
        if (!GetModuleFileNameW(nullptr, actual, MAX_PATH) || !GetWindowsDirectoryW(windows, MAX_PATH))
            return false;
        const std::wstring expected = std::wstring(windows) +
            L"\\SystemApps\\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\\StartMenuExperienceHost.exe";
        return _wcsicmp(actual, expected.c_str()) == 0;
    }

    void Attach()
    {
        if (!IsStartMenuHost() || !ui::Core::CoreWindow::GetForCurrentThread())
            return;
        auto window = xaml::Window::Current();
        if (!window || !window.Content())
            return;
        static Backgrounds* attached{};
        if (attached)
            return;
        HANDLE mapping = OpenFileMappingW(FILE_MAP_WRITE, FALSE, SessionName(GetCurrentProcessId()).c_str());
        if (!mapping)
            return;
        auto session = static_cast<Session*>(MapViewOfFile(mapping, FILE_MAP_WRITE, 0, 0, sizeof(Session)));
        if (!session)
        {
            CloseHandle(mapping);
            return;
        }
        try
        {
            HMODULE module{};
            if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                reinterpret_cast<LPCWSTR>(&Attach), &module))
                winrt::throw_last_error();
            attached = new Backgrounds(mapping, session);
            attached->Start();
            InterlockedExchange(&session->ready, 1);
        }
        catch (...)
        {
            InterlockedExchange(&session->ready, winrt::to_hresult());
            if (attached)
                delete std::exchange(attached, nullptr);
            else
            {
                UnmapViewOfFile(session);
                CloseHandle(mapping);
            }
        }
    }
}

extern "C" __declspec(dllexport) LRESULT CALLBACK ConnectStartMenu(int code, WPARAM removed, LPARAM data)
{
    if (code == HC_ACTION && removed == PM_REMOVE)
    {
        const auto& message = *reinterpret_cast<const MSG*>(data);
        if (!message.hwnd && message.message == ConnectionMessage())
        {
            try { Attach(); }
            catch (...) {}
        }
    }
    return CallNextHookEx(nullptr, code, removed, data);
}
