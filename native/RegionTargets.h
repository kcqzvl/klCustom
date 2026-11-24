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

inline TargetHit FindTarget(xaml::FrameworkElement const& element)
{
    auto name = element.Name();
    if (name == L"RecommendedList" && (element.try_as<controls::GridView>() || element.try_as<controls::ListView>()))
        return {2};
    if ((name == L"AllAppsGrid" && element.try_as<controls::GridView>()) ||
        (name == L"AppsList" && winrt::get_class_name(element) == L"StartDocked.AllAppsGridListView"))
        return {3};
    if (name == L"NavPanePlaceholder" && element.try_as<controls::Grid>())
        return {5};

    if (name == L"Root" && element.try_as<controls::Grid>())
    {
        auto parent = Parent(element);
        if (Named(parent, L"StartMenuPinnedList") && winrt::get_class_name(parent) == L"StartMenu.PinnedList")
            return {1};
    }
    if (!element.try_as<controls::Border>())
        return {};

    auto parent = Parent(element);
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
        if (search && winrt::get_class_name(search) == L"StartMenu.SearchBoxToggleButton")
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
