#pragma once

#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>

namespace xaml = winrt::Windows::UI::Xaml;
namespace controls = xaml::Controls;

struct TargetHit {
    int region{-1};
    bool overlay{};
};

inline xaml::FrameworkElement Parent(xaml::FrameworkElement const& element)
{
    if (element)
        return xaml::Media::VisualTreeHelper::GetParent(element).try_as<xaml::FrameworkElement>();
    return nullptr;
}

inline bool Named(xaml::FrameworkElement const& element, std::wstring_view name)
{
    return element && element.Name() == name;
}

inline bool IsType(xaml::FrameworkElement const& element, std::wstring_view type)
{
    return element && winrt::get_class_name(element) == type;
}

inline TargetHit FindWindows10Target(xaml::FrameworkElement const& element, winrt::hstring const& type)
{
    if (type == L"StartUI.TileGridView") return {1};
    if (type == L"StartUI.AllAppsGridListView") return {3};
    if (type == L"StartUI.NavigationPaneGrid") return {5};
    if (element.Name() != L"BackgroundElement" || !element.try_as<controls::Border>())
        return {};

    for (auto parent = Parent(element); parent; parent = Parent(parent))
    {
        if (IsType(parent, L"StartUI.StartSizingFrame") || IsType(parent, L"StartUI.SplitViewFrame"))
            return {0};
        if (IsType(parent, L"StartUI.TileGridView") || IsType(parent, L"StartUI.AllAppsPane")
            || IsType(parent, L"StartUI.NavigationPaneView"))
            break;
    }
    return {};
}

inline TargetHit FindTarget(xaml::FrameworkElement const& element)
{
    auto name = element.Name();
    auto type = winrt::get_class_name(element);
    if (std::wstring_view(type).starts_with(L"StartUI.") || name == L"BackgroundElement")
        return FindWindows10Target(element, type);

    if (name == L"RecommendedList" && (element.try_as<controls::GridView>() || element.try_as<controls::ListView>()))
        return {2};
    if ((name == L"AllAppsGrid" && element.try_as<controls::GridView>()) ||
        (name == L"AppsList" && type == L"StartDocked.AllAppsGridListView"))
        return {3};
    if (name == L"NavPanePlaceholder" && element.try_as<controls::Grid>())
        return {5};

    if (name == L"Root" && element.try_as<controls::Grid>())
    {
        auto parent = Parent(element);
        if (Named(parent, L"StartMenuPinnedList") && IsType(parent, L"StartMenu.PinnedList"))
            return {1};
    }
    if (!element.try_as<controls::Border>())
        return {};

    auto parent = Parent(element);
    if ((name == L"AcrylicBorder" || name == L"AcrylicOverlay") && Named(parent, L"RootContent"))
    {
        auto root = Parent(parent);
        if (Named(root, L"RootGrid"))
        {
            auto frame = Parent(root);
            if (Named(frame, L"RootPanel")) frame = Parent(frame);
            if (IsType(frame, L"StartDocked.LauncherFrame")) return {0, name == L"AcrylicOverlay"};
        }
    }
    if (name == L"AcrylicBorder")
    {
        if (Named(parent, L"CompanionRoot")) return {6};
        if (Named(parent, L"MainMenu")) return {0};
    }
    if (name == L"AcrylicOverlay" && Named(parent, L"MainContent"))
    {
        auto root = Parent(parent);
        if (Named(root, L"CompanionRoot")) return {6, true};
        if (Named(root, L"MainMenu")) return {0, true};
    }
    if (name == L"BorderElement" && parent.try_as<controls::Grid>())
    {
        auto search = Parent(parent);
        if (IsType(search, L"StartMenu.SearchBoxToggleButton") ||
            (Named(search, L"StartMenuSearchBox") && IsType(search, L"StartDocked.SearchBoxToggleButton")))
            return {4};
    }
    return {};
}

inline xaml::DependencyProperty BackgroundProperty(xaml::FrameworkElement const& element)
{
    if (element.try_as<controls::Border>()) return controls::Border::BackgroundProperty();
    if (element.try_as<controls::Panel>()) return controls::Panel::BackgroundProperty();
    if (element.try_as<controls::Control>()) return controls::Control::BackgroundProperty();
    return nullptr;
}
