/*
 * Recreates Epic's platform switcher controls in static/offline pages.
 */
(function () {
	if (window.__mhtmlEpicSwitchReady) return;
	window.__mhtmlEpicSwitchReady = true;

	// Normalize captured platform keys into labels that are shown in the custom dropdown.
	const optionNames = {
		windows: "Windows",
		linux: "Linux",
		apple: "Mac",
		mac: "Mac",
		macos: "Mac"
	};

	const style = document.createElement("style");
	style.textContent = `
		ng-select.mhtml-switch-ready .ng-select-container { cursor: pointer; }
		ng-select.mhtml-switch-ready .ng-input input { pointer-events: none; }
		ng-select.mhtml-switch-ready.ng-select-opened .ng-arrow { transform: rotate(180deg); }
		ng-select.mhtml-switch-ready .ng-dropdown-panel {
			display: none;
			opacity: 1;
			top: calc(100% + 4px);
			left: 0;
			min-width: 100%;
		}
		ng-select.mhtml-switch-ready.ng-select-opened .ng-dropdown-panel { display: block; }
		ng-select.mhtml-switch-ready .ng-option {
			padding: 10px 14px;
			background: #2b2b31;
			color: #eee;
		}
		ng-select.mhtml-switch-ready .ng-option:hover,
		ng-select.mhtml-switch-ready .ng-option.marked { background: #3a3a45; }
		ng-select.mhtml-switch-ready .ng-option.selected::before {
			content: "\\2713";
			display: inline-block;
			width: 18px;
			color: #58c7ff;
		}
		ng-select.mhtml-switch-ready .ng-option:not(.selected)::before {
			content: "";
			display: inline-block;
			width: 18px;
		}
	`;
	document.head.appendChild(style);

	const views = Array.from(document.querySelectorAll("block-switch-view"));
	const controls = Array.from(document.querySelectorAll("block-switch-control"));
	if (!views.length || !controls.length) return;

	const options = collectOptions(views);
	if (!options.length) return;

	// Use the captured selected label when possible; otherwise fall back to the first available variant.
	let selected = readInitialSelection() || options[0].key;
	if (!options.some(option => option.key === selected)) selected = options[0].key;

	for (const control of controls) {
		setupControl(control);
	}
	applySelection(selected);

	document.addEventListener("click", event => {
		if (!event.target.closest("ng-select.mhtml-switch-ready")) closeAll();
	});

	function collectOptions(items) {
		// The source page stores variants on block-switch-view nodes; duplicate keys are ignored.
		const map = new Map();
		for (const view of items) {
			const key = readViewOption(view);
			if (!key || map.has(key)) continue;
			map.set(key, optionNames[key] || toTitle(key));
		}
		return Array.from(map, ([key, label]) => ({ key, label }));
	}

	function readInitialSelection() {
		const label = document.querySelector("block-switch-control .ng-value-label")?.textContent?.trim();
		if (!label) return "";
		return normalizeOption(label);
	}

	function readViewOption(view) {
		const icon = view.querySelector(".block-switch-option-icon");
		if (!icon) return "";
		for (const className of icon.classList) {
			if (!className.startsWith("icon-")) continue;
			const key = normalizeOption(className.slice(5));
			if (optionNames[key]) return key;
		}
		return "";
	}

	function setupControl(control) {
		const select = control.querySelector("ng-select");
		if (!select) return;

		select.classList.add("mhtml-switch-ready");
		select.setAttribute("tabindex", "0");
		select.setAttribute("role", "button");

		let panel = select.querySelector(":scope > .ng-dropdown-panel");
		if (!panel) {
			panel = document.createElement("div");
			panel.className = "ng-dropdown-panel mhtml-switch-panel";
			select.appendChild(panel);
		}

		panel.innerHTML = "";
		const items = document.createElement("div");
		items.className = "ng-dropdown-panel-items scroll-host";
		panel.appendChild(items);

		for (const option of options) {
			const item = document.createElement("div");
			item.className = "ng-option";
			item.dataset.option = option.key;
			item.innerHTML = `<span class="ng-option-label"></span>`;
			item.querySelector(".ng-option-label").textContent = option.label;
			item.addEventListener("click", event => {
				event.stopPropagation();
				selected = option.key;
				applySelection(selected);
				closeAll();
			});
			items.appendChild(item);
		}

		select.addEventListener("click", event => {
			event.stopPropagation();
			if (options.length <= 1) return;
			const open = !select.classList.contains("ng-select-opened");
			closeAll();
			if (open) openSelect(select);
		});
	}

	function applySelection(key) {
		// Selection updates both controls and content panes so the static page behaves like the live site.
		const label = options.find(option => option.key === key)?.label || toTitle(key);
		for (const control of controls) {
			const labelEl = control.querySelector(".ng-value-label");
			if (labelEl) labelEl.textContent = label;

			const input = control.querySelector("input[role='combobox']");
			if (input) input.setAttribute("aria-expanded", "false");

			for (const item of control.querySelectorAll(".ng-option")) {
				const active = item.dataset.option === key;
				item.classList.toggle("selected", active);
				item.classList.toggle("marked", active);
			}
		}

		for (const view of views) {
			const viewOption = readViewOption(view);
			view.hidden = !!viewOption && viewOption !== key;
		}
	}

	function openSelect(select) {
		select.classList.add("ng-select-opened");
		select.querySelector("input[role='combobox']")?.setAttribute("aria-expanded", "true");
	}

	function closeAll() {
		for (const select of document.querySelectorAll("ng-select.mhtml-switch-ready.ng-select-opened")) {
			select.classList.remove("ng-select-opened");
			select.querySelector("input[role='combobox']")?.setAttribute("aria-expanded", "false");
		}
	}

	function normalizeOption(value) {
		return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, "");
	}

	function toTitle(value) {
		return value ? value[0].toUpperCase() + value.slice(1) : value;
	}
})();
