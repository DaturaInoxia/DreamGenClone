// Keep for any legacy Blazor JS interop calls
window.sidebarStateGet = function () {
    return localStorage.getItem('sidebar-collapsed') === '1';
};

window.sidebarStateSet = function (collapsed) {
    localStorage.setItem('sidebar-collapsed', collapsed ? '1' : '0');
};

// Pure-JS sidebar toggle (works without Blazor interactivity)
window.sidebarToggle = function () {
    var page = document.querySelector('.page');
    if (!page) return;
    var collapsed = page.classList.toggle('sidebar-collapsed');
    localStorage.setItem('sidebar-collapsed', collapsed ? '1' : '0');
    var btn = document.querySelector('.sidebar-toggle-btn');
    if (btn) btn.textContent = collapsed ? '\u203a' : '\u2039';
};

// Pure-JS nav group expand/collapse
window.navGroupToggle = function (headerEl) {
    var group = headerEl.closest('.nav-group');
    if (!group) return;
    var expanded = group.classList.toggle('expanded');
    var labelEl = group.querySelector('.nav-group-label');
    if (labelEl) {
        var key = 'nav-group-' + labelEl.textContent.trim().toLowerCase();
        sessionStorage.setItem(key, expanded ? '1' : '0');
    }
};

function _applyStoredNavState() {
    // Sidebar collapsed state
    var page = document.querySelector('.page');
    if (page) {
        var sidebarCollapsed = localStorage.getItem('sidebar-collapsed') === '1';
        page.classList.toggle('sidebar-collapsed', sidebarCollapsed);
        var btn = document.querySelector('.sidebar-toggle-btn');
        if (btn) btn.textContent = sidebarCollapsed ? '\u203a' : '\u2039';
    }
    // Nav group expanded state (defaults: play + content expanded)
    document.querySelectorAll('.nav-group').forEach(function (group) {
        var labelEl = group.querySelector('.nav-group-label');
        if (!labelEl) return;
        var label = labelEl.textContent.trim().toLowerCase();
        var stored = sessionStorage.getItem('nav-group-' + label);
        var defaultExpanded = (label === 'play' || label === 'content');
        var expanded = stored !== null ? stored === '1' : defaultExpanded;
        group.classList.toggle('expanded', expanded);
    });
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', _applyStoredNavState);
} else {
    _applyStoredNavState();
}
document.addEventListener('blazor:navigated', _applyStoredNavState);
