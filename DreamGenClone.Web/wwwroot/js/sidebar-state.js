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
    // Also sync mobile navbar-toggler checkbox state
    var toggler = document.getElementById('navbar-toggler');
    if (toggler) {
        toggler.checked = !collapsed;
        localStorage.setItem('navbar-toggler-checked', toggler.checked ? '1' : '0');
    }
};

// Pure-JS nav group expand/collapse
window.navGroupToggle = function (headerEl) {
    var group = headerEl.closest('.nav-group');
    if (!group) return;
    var expanded = group.classList.toggle('expanded');
    var labelEl = group.querySelector('.nav-group-label');
    if (labelEl) {
        var key = 'nav-group-' + labelEl.textContent.trim().toLowerCase();
        localStorage.setItem(key, expanded ? '1' : '0');
    }
};

function _applyStoredNavState() {
    // Sidebar collapsed state — default to collapsed on first visit
    var page = document.querySelector('.page');
    if (page) {
        var stored = localStorage.getItem('sidebar-collapsed');
        var sidebarCollapsed = stored !== null ? stored === '1' : true;
        page.classList.toggle('sidebar-collapsed', sidebarCollapsed);
        var btn = document.querySelector('.sidebar-toggle-btn');
        if (btn) btn.textContent = sidebarCollapsed ? '\u203a' : '\u2039';
    }
    // Mobile navbar-toggler checkbox state — persist across navigation
    var toggler = document.getElementById('navbar-toggler');
    if (toggler) {
        var togglerStored = localStorage.getItem('navbar-toggler-checked');
        var togglerChecked = togglerStored !== null ? togglerStored === '1' : false;
        toggler.checked = togglerChecked;
    }
    // Nav group expanded state — use localStorage for persistence across sessions
    // Find active link first to ensure its group stays expanded
    var activeLink = document.querySelector('.nav-link.active');
    var activeGroupLabel = null;
    if (activeLink) {
        var activeGroup = activeLink.closest('.nav-group');
        if (activeGroup) {
            var labelEl = activeGroup.querySelector('.nav-group-label');
            if (labelEl) {
                activeGroupLabel = labelEl.textContent.trim().toLowerCase();
            }
        }
    }
    document.querySelectorAll('.nav-group').forEach(function (group) {
        var labelEl = group.querySelector('.nav-group-label');
        if (!labelEl) return;
        var label = labelEl.textContent.trim().toLowerCase();
        var stored = localStorage.getItem('nav-group-' + label);
        var defaultExpanded = (label === 'play' || label === 'content');
        var expanded = stored !== null ? stored === '1' : defaultExpanded;
        // Always expand the group containing the active link
        if (label === activeGroupLabel) {
            expanded = true;
        }
        group.classList.toggle('expanded', expanded);
    });
}

// Use MutationObserver to watch for DOM changes and re-apply state
var navObserver = new MutationObserver(function (mutations) {
    mutations.forEach(function (mutation) {
        if (mutation.type === 'childList') {
            // Check if any nav-group elements were added
            mutation.addedNodes.forEach(function (node) {
                if (node.nodeType === 1) { // Element node
                    if (node.classList && node.classList.contains('nav-group')) {
                        _applyStoredNavState();
                    } else if (node.querySelectorAll) {
                        var navGroups = node.querySelectorAll('.nav-group');
                        if (navGroups.length > 0) {
                            _applyStoredNavState();
                        }
                    }
                }
            });
        }
    });
});

// Start observing the document for nav-group changes
function startNavObserver() {
    var navContainer = document.querySelector('.nav-scrollable');
    if (navContainer) {
        navObserver.observe(navContainer, { childList: true, subtree: true });
    }
}

// Aggressive state restoration - run multiple times after navigation
function aggressiveStateRestore() {
    _applyStoredNavState();
    // Run again after a short delay to catch any Blazor re-renders
    setTimeout(_applyStoredNavState, 50);
    setTimeout(_applyStoredNavState, 150);
    setTimeout(_applyStoredNavState, 300);
    setTimeout(_applyStoredNavState, 500);
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', function () {
        _applyStoredNavState();
        startNavObserver();
    });
} else {
    _applyStoredNavState();
    startNavObserver();
}
document.addEventListener('blazor:navigated', aggressiveStateRestore);

// Also listen for route changes via URL
var lastUrl = location.href;
new MutationObserver(function () {
    var url = location.href;
    if (url !== lastUrl) {
        lastUrl = url;
        aggressiveStateRestore();
    }
}).observe(document, { subtree: true, childList: true });

// Listen for mobile navbar-toggler changes and persist state
document.addEventListener('change', function (e) {
    if (e.target && e.target.id === 'navbar-toggler') {
        localStorage.setItem('navbar-toggler-checked', e.target.checked ? '1' : '0');
    }
});
