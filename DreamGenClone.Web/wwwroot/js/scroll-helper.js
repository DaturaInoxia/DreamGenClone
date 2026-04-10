window.scrollHelper = {
    saveScrollPosition: function (key) {
        sessionStorage.setItem(key, window.scrollY.toString());
    },
    restoreScrollPosition: function (key) {
        var pos = sessionStorage.getItem(key);
        if (pos) {
            window.scrollTo(0, parseInt(pos, 10));
            sessionStorage.removeItem(key);
        }
    }
};
