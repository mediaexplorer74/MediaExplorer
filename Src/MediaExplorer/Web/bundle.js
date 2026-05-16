// This file is part of Messenger UWP.
// Copyright (C) 2019 Sylvain Bruyère
//
// Messenger UWP is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3.
//
// Messenger UWP is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Messenger UWP.  If not, see <https://www.gnu.org/licenses/>.


if (window.Windows && Windows.UI.Popups) {
    window.alert = function (message) {
        var dialog = new Windows.UI.Popups.MessageDialog(message);
        dialog.showAsync();
    };
}
// This file is part of Messenger UWP.
// Copyright (C) 2019 Sylvain Bruyère
//
// Messenger UWP is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3.
//
// Messenger UWP is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Messenger UWP.  If not, see <https://www.gnu.org/licenses/>.


/**
 * Dispatcher helps to handle events from the global (window) context applied to specific DOM elements.
 * Use it when we can't safely add listeners on DOM elements (like with React, Angular or Vue projects,
 * as the DOM can drastically change).
 */
var Dispatcher = {};

/**
 * Listen and dispatch click events to subscribers.
 */
Dispatcher.Click = (function () {
    var self = {};

    var bubblingSubscribers = [];
    var capturingSubscribers = [];

    /**
     * Subscribes to clicks on elements that matches to a selector (and don't matches the
     * exclude selector, if specified).
     * @param useCapture refers to the dispatch order as described here :
     * https://developer.mozilla.org/en-US/docs/Web/API/EventTarget/addEventListener#Syntax
     */
    self.subscribe = function (selector, callback, useCapture, excludeSelector) {
        var obj = { selector: selector, excludeSelector: excludeSelector, callback: callback };
        if (useCapture)
            capturingSubscribers.push(obj);
        else
            bubblingSubscribers.push(obj);
    };

    /**
     * Checks if the click matches a subscribed selector.
     */
    function checkSubscribers(event, subscribers) {
        if (!event.isTrusted)
            return;

        for (var i = 0; i < subscribers.length; i++) {
            var subscriber = subscribers[i];
            if (event.target.closest(subscriber.selector)) {
                var exclude = subscriber.excludeSelector && event.target.closest(subscriber.excludeSelector);
                if (!exclude)
                    subscriber.callback();
            }
        }
    }

    // Listen every clicks on the window by bubbling.
    window.addEventListener("click", function (event) {
        checkSubscribers(event, bubblingSubscribers);
    }, false);

    // Listen every clicks on the window by capturing.
    window.addEventListener("click", function (event) {
        checkSubscribers(event, capturingSubscribers);
    }, true);

    return self;
})();
// This file is part of Messenger UWP.
// Copyright (C) 2019 Sylvain Bruyère
//
// Messenger UWP is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3.
//
// Messenger UWP is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Messenger UWP.  If not, see <https://www.gnu.org/licenses/>.


var DOM = {};

DOM.getById = function (id) {
    return document.getElementById(id);
};

DOM.getByClass = function (name) {
    return document.getElementsByClassName(name)[0];
};
DOM.getAllByClass = function (name) {
    return document.getElementsByClassName(name);
};

DOM.getByTag = function (tag) {
    return document.getElementsByTagName(tag)[0];
};
DOM.getAllByTag = function (tag) {
    return document.getElementsByTagName(tag);
};

DOM.getBySelector = function (selector) {
    return document.querySelector(selector);
};
DOM.getAllBySelector = function (selector) {
    return document.querySelectorAll(selector);
};
// This file is part of Messenger UWP.
// Copyright (C) 2019 Sylvain Bruyère
//
// Messenger UWP is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3.
//
// Messenger UWP is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Messenger UWP.  If not, see <https://www.gnu.org/licenses/>.


var Navigation = (function () {
    var self = {};

    var navManager = null;
    var stack = [];

    /**
     * Notify that we navigate to another page and specify an action to execute on pop.
     * The shouldShowBackButtonFunc is an optional function that should return if the back button need to be visible.
     */
    self.pushToStack = function (popCallback, shouldShowBackButtonFunc) {
        stack.push({ popCallback: popCallback, shouldShowBackButtonFunc: shouldShowBackButtonFunc });
        self.updateBackButtonVisibility();
    };

    /**
     * Trigger the action to execute on pop.
     */
    self.popFromStack = function () {
        var popped = stack.pop();
        if (popped) {
            popped.popCallback();
            self.updateBackButtonVisibility();
            return true;
        }
        return false;
    };

    /**
     * Update the Windows back button visibility.
     */
    self.updateBackButtonVisibility = function () {
        if (navManager) {
            var total = stack.length;
            for (var i = 0; i < stack.length; i++) {
                var item = stack[i];
                if (item.shouldShowBackButtonFunc && item.shouldShowBackButtonFunc() === false) {
                    total--;
                }
            }

            navManager.appViewBackButtonVisibility = total > 0 ?
                Windows.UI.Core.AppViewBackButtonVisibility.visible :
                Windows.UI.Core.AppViewBackButtonVisibility.collapsed;
        }
    };

    // Listen Windows back events
    if (window.Windows && Windows.UI.Core) {
        navManager = Windows.UI.Core.SystemNavigationManager.getForCurrentView();
        navManager.addEventListener("backrequested", function (event) {
            event.handled = self.popFromStack();
        });
    }

    return self;
}());
// This file is part of Messenger UWP.
// Copyright (C) 2019 Sylvain Bruyère
//
// Messenger UWP is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3.
//
// Messenger UWP is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Messenger UWP.  If not, see <https://www.gnu.org/licenses/>.


if (!Element.prototype.matches) {
    Element.prototype.matches = Element.prototype.msMatchesSelector || Element.prototype.webkitMatchesSelector;
}

if (!Element.prototype.closest) {
    Element.prototype.closest = function (selectors) {
        var element = this;

        do {
            if (element.matches(selectors)) {
                return element;
            }
        } while (element = element.parentElement);

        return null;
    };
}
// This file is part of Messenger UWP.
// Copyright (C) 2019-2020 Sylvain Bruyère
//
// Messenger UWP is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3.
//
// Messenger UWP is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Messenger UWP.  If not, see <https://www.gnu.org/licenses/>.


/**
 * ReactInternal provides functions to interact with React components from pure JavaScript at the runtime.
 */
var ReactInternal = (function () {
    var self = {};

    /**
     * Gets the internal React instance of a DOM node, if it exists.
     */
    function getInternalInstance(domNode) {
        var keys = Object.keys(domNode);
        for (var keyIndex in keys) {
            var key = keys[keyIndex];
            if (key.startsWith("__reactFiber$") || key.startsWith("__reactInternalInstance$")) {
                return domNode[key];
            }
        }
        return null;
    }

    /**
     * Find the associated Component instance of a DOM node, if it exists, else return null.
     */
    self.findComponent = function (domNode) {
        var instance = getInternalInstance(domNode);
        if (instance && instance.return && !(instance.return.stateNode instanceof Node))
            return instance.return.stateNode;

        return null;
    };

    /**
     * Find the associated Component instance of a DOM node, if it exists.
     * Else, try to find a parent Component instance.
     * If it reaches the root without finding anything, return null.
     */
    self.findClosestComponent = function (domNode) {
        var instance = getInternalInstance(domNode);
        if (instance) {
            while (instance.return && (!instance.return.stateNode || instance.return.stateNode instanceof Node)) {
                instance = instance.return;
            }
            if (instance.return)
                return instance.return.stateNode;
        }

        return null;
    };

    return self;
})();
// This file is part of Messenger UWP.
// Copyright (C) 2019 Sylvain Bruyère
//
// Messenger UWP is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3.
//
// Messenger UWP is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Messenger UWP.  If not, see <https://www.gnu.org/licenses/>.


(function () {
    // Set the viewport for mobile devices.
    var meta = document.createElement("meta");
    meta.name = "viewport";
    meta.content = "width=device-width, initial-scale=1";
    DOM.getByTag("head").appendChild(meta);

    // As we change the user agent, we need to ensure that the CSS fixes for Edge are applied.
    var bodyClassList = document.body.classList;
    if (!bodyClassList.contains("edge"))
        bodyClassList.add("edge");
})();

var MessengerPWA = {};
MessengerPWA.Views = {};
// This file is part of Messenger UWP.
// Copyright (C) 2019 Sylvain Bruyère
//
// Messenger UWP is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3.
//
// Messenger UWP is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Messenger UWP.  If not, see <https://www.gnu.org/licenses/>.


MessengerPWA.Selectors = {
    ROOT_CONTAINER: "._2sdm > ._li",
    MASTER_DETAIL_CONTAINER: "_4sp8",

    MASTER_VIEW: "_1enh",
    MASTER_VIEW_V2: "_7q1s",

    INFO_PANEL: "_4_j5",
    INFO_PANEL_CONTENT: "_4_j9",
    INFO_PANEL_BUTTON: "[data-testid=info_panel_button]",
    INFO_PANEL_SEARCH_BUTTON: "._3tkv > :not(._1lix) + ._1lix ._3szo:first-child"
};
// This file is part of Messenger UWP.
// Copyright (C) 2019 Sylvain Bruyère
//
// Messenger UWP is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3.
//
// Messenger UWP is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Messenger UWP.  If not, see <https://www.gnu.org/licenses/>.


MessengerPWA.Version = (function () {
    var masterView = DOM.getByClass(MessengerPWA.Selectors.MASTER_VIEW);
    if (masterView) {
        var messengerV2 = masterView.classList.contains(MessengerPWA.Selectors.MASTER_VIEW_V2);
        return messengerV2 ? 2 : 1;
    }

    return null;
})();
// This file is part of Messenger UWP.
// Copyright (C) 2019 Sylvain Bruyère
//
// Messenger UWP is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3.
//
// Messenger UWP is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Messenger UWP.  If not, see <https://www.gnu.org/licenses/>.


MessengerPWA.Views.InfoPanel = (function () {
    var self = {};

    /**
     * Gets if the info panel is visible or not.
     */
    self.isVisible = function () {
        var panel = DOM.getByClass(MessengerPWA.Selectors.INFO_PANEL);
        return Boolean(panel && !panel.classList.contains("hidden_elem"));
    };

    /**
     * Change the info panel visibily. Show it if hidden and hide it if shown.
     */
    self.toggleVisibility = function () {
        var btn = DOM.getBySelector(MessengerPWA.Selectors.INFO_PANEL_BUTTON);
        if (btn)
            btn.click();
    };

    /**
     * Show the info panel.
     */
    self.show = function () {
        if (!self.isVisible())
            self.toggleVisibility();
    };

    /**
     * Hide the info panel.
     */
    self.hide = function () {
        if (self.isVisible())
            self.toggleVisibility();
    };

    // Subscribe to clicks on info panel button.
    Dispatcher.Click.subscribe(MessengerPWA.Selectors.INFO_PANEL_BUTTON, function () {
        Navigation.pushToStack(self.hide);
    }, true);

    // Subscribe to clicks outside the info panel content.
    Dispatcher.Click.subscribe("." + MessengerPWA.Selectors.INFO_PANEL, function () {
        Navigation.popFromStack();
    }, false, "." + MessengerPWA.Selectors.INFO_PANEL_CONTENT);

    // Subscribe to clicks on search button.
    Dispatcher.Click.subscribe(MessengerPWA.Selectors.INFO_PANEL_SEARCH_BUTTON, function () {
        Navigation.popFromStack();
    });

    return self;
})();
// This file is part of Messenger UWP.
// Copyright (C) 2019 Sylvain Bruyère
//
// Messenger UWP is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3.
//
// Messenger UWP is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Messenger UWP.  If not, see <https://www.gnu.org/licenses/>.


MessengerPWA.Views.MasterDetail = (function () {
    var self = {};

    var container = DOM.getByClass(MessengerPWA.Selectors.MASTER_DETAIL_CONTAINER);
    var root = DOM.getBySelector(MessengerPWA.Selectors.ROOT_CONTAINER);
    var rootComponent = null;

    /**
     * Gets if the detail view has content or not.
     */
    self.hasDetailView = function () {
        return rootComponent.state.reasonState.detailView != null;
    };

    /**
     * Clears the detail view content.
     * Based on a similar code for removing a conversation.
     */
    function clearDetailView() {
        rootComponent.setState(function (prevState) {
            prevState.reasonState.activeThreadID = null;
            prevState.reasonState.detailView = null;
            prevState.reasonState.serverThreadID = null;
            return prevState;
        }, function () {
            var url = getUrlWithoutThread();
            window.history.pushState(url, "", url);
            updateViews();
        });
    }

    /**
     * Gets the current URL without the thread name, if existing.
     */
    function getUrlWithoutThread() {
        var url = window.location.href;
        var index = url.indexOf("/t/");
        if (index > -1)
            url = url.substring(0, index) + "/" + window.location.search;
        return url;
    }

    /**
     * Updates the displayed views.
     */
    function updateViews() {
        if (window.innerWidth < 700) {
            if (self.hasDetailView()) {
                container.classList.remove("master");
                container.classList.add("detail");
            }
            else {
                container.classList.add("master");
                container.classList.remove("detail");
            }
        }
        else {
            container.classList.remove("master");
            container.classList.remove("detail");
        }
    }

    /**
     * Triggered after the "MessengerReact" component update.
     */
    function componentDidUpdate(prevProps, prevState) {
        // If the detail view content appears.
        if (prevState.reasonState.detailView == null && this.state.reasonState.detailView != null) {
            // Ensures the info panel is closed and updates views.
            MessengerPWA.Views.InfoPanel.hide();
            updateViews();

            Navigation.pushToStack(clearDetailView);
        }
        // If the detail view content disappears.
        else if (prevState.reasonState.detailView != null && this.state.reasonState.detailView == null) {
            Navigation.popFromStack();
        }
    }

    if (root && container) {
        // Gets the root "MessengerReact" component.
        rootComponent = ReactInternal.findClosestComponent(root);

        // Intercepts the componentDidUpdate calls to know when the state changes.
        var originalComponentDidUpdate = rootComponent.componentDidUpdate.bind(rootComponent);
        rootComponent.componentDidUpdate = function (prevProps, prevState, snapshot) {
            originalComponentDidUpdate(prevProps, prevState, snapshot);
            componentDidUpdate.call(rootComponent, prevProps, prevState);
        };

        clearDetailView();

        window.addEventListener("resize", updateViews, false);
    }

    return self;
})();