/*
 * Recreates Epic's platform switcher controls in static/offline pages.
 */
(function () {
	if (window.__mhtmlEpicSwitchReady) return;
	window.__mhtmlEpicSwitchReady = true;

	const style = document.createElement("style");
	style.textContent = `
		block-switch-control.mhtml-switch-ready {
			position: relative;
			z-index: 20;
		}
		block-switch-control.mhtml-switch-ready ng-select {
			position: relative;
			display: block;
		}
		ng-select.mhtml-switch-ready .ng-select-container {
			cursor: pointer;
			position: relative;
		}
		ng-select.mhtml-switch-ready .ng-value-container {
			opacity: 0;
			pointer-events: none;
		}
		ng-select.mhtml-switch-ready .mhtml-switch-trigger-label {
			position: absolute;
			inset: 0 24px 0 0;
			display: flex;
			align-items: center;
			justify-content: center;
			min-width: 0;
			padding-left: 10px;
			overflow: hidden;
			color: inherit;
			font: inherit;
			pointer-events: none;
			text-align: center;
			text-overflow: ellipsis;
			white-space: nowrap;
		}
		ng-select.mhtml-switch-ready .ng-arrow-wrapper {
			position: relative;
			z-index: 1;
		}
		ng-select.mhtml-switch-ready .ng-input input { pointer-events: none; }
		ng-select.mhtml-switch-ready.ng-select-opened .ng-arrow { transform: rotate(180deg); }
		ng-select.mhtml-switch-ready .ng-dropdown-panel {
			display: none;
			position: absolute;
			z-index: 10000;
			opacity: 1;
			top: calc(100% + 4px);
			left: 0;
			min-width: 100%;
			width: max-content;
			max-width: min(320px, 90vw);
			max-height: min(320px, 60vh);
			overflow: auto;
			border: 1px solid #454956;
			border-radius: 4px;
			box-shadow: 0 12px 28px rgba(0, 0, 0, 0.35);
		}
		ng-select.mhtml-switch-ready.ng-select-opened .ng-dropdown-panel { display: block; }
		ng-select.mhtml-switch-ready .ng-option {
			padding: 10px 14px;
			background: #2b2b31;
			color: #eee;
			cursor: pointer;
			white-space: nowrap;
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
	if (!controls.length) return;

	const hostOptions = collectHostOptions();
	const viewOptions = collectOptions(views);
	const options = hostOptions.length ? hostOptions : viewOptions;
	if (!options.length) return;

	// Use the captured selected label when possible; otherwise fall back to the first available variant.
	let selected = readHostSelection(options) || readInitialSelection() || options[0].key;
	if (!options.some(option => option.key === selected)) selected = options[0].key;
	const viewKeyAlias = buildViewKeyAlias();

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
			const option = readViewOption(view);
			if (!option || map.has(option.key)) continue;
			map.set(option.key, option.label);
		}
		return Array.from(map, ([key, label]) => ({ key, label }));
	}

	function collectHostOptions() {
		const items = Array.isArray(window.__mhtmlSwitchOptions) ? window.__mhtmlSwitchOptions : [];
		const options = [];
		const seen = new Set();
		for (const item of items) {
			const key = normalizeOption(item?.key || "");
			const path = typeof item?.path === "string" ? item.path : "";
			if (!key || !path || seen.has(key)) continue;
			seen.add(key);
			options.push({
				key,
				label: cleanLabel(item?.label) || formatOptionLabel(item?.key || key),
				path,
				current: item?.current === true
			});
		}
		return options;
	}

	function readHostSelection(items) {
		return items.find(option => option.current)?.key || "";
	}

	function readInitialSelection() {
		const label = document.querySelector("block-switch-control .ng-value-label")?.textContent?.trim();
		if (!label) return "";
		return normalizeOption(label);
	}

	function readViewOption(view) {
		const icon = view.querySelector(".block-switch-option-icon");
		if (!icon) return null;
		for (const className of icon.classList) {
			if (!className.startsWith("icon-")) continue;
			const raw = className.slice(5);
			const key = normalizeOption(raw);
			if (key) return { key, label: formatOptionLabel(raw) };
		}
		return null;
	}

	function buildViewKeyAlias() {
		const map = new Map();
		if (!hostOptions.length) return map;

		const viewKeys = new Set();
		for (const view of views) {
			const option = readViewOption(view);
			if (option?.key) viewKeys.add(option.key);
		}

		if (viewKeys.size !== 1) return map;
		const onlyViewKey = Array.from(viewKeys)[0];
		if (!options.some(option => option.key === onlyViewKey)) {
			map.set(onlyViewKey, selected);
		}
		return map;
	}

	function setupControl(control) {
		const select = control.querySelector("ng-select");
		if (!select) return;

		control.classList.add("mhtml-switch-ready");
		select.classList.add("mhtml-switch-ready");
		select.setAttribute("tabindex", "0");
		select.setAttribute("role", "button");
		ensureTriggerLabel(select);

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
				if (option.path && !option.current && navigateToOption(option)) {
					closeAll();
					return;
				}
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

	function ensureTriggerLabel(select) {
		const container = select.querySelector(":scope > .ng-select-container");
		if (!container) return null;

		let label = container.querySelector(":scope > .mhtml-switch-trigger-label");
		if (!label) {
			label = document.createElement("span");
			label.className = "mhtml-switch-trigger-label";
			container.appendChild(label);
		}
		return label;
	}

	function navigateToOption(option) {
		if (!window.chrome || !chrome.webview || !option.path) return false;
		chrome.webview.postMessage({
			type: "OpenDocument",
			path: option.path,
			fragment: location.hash ? location.hash.slice(1) : ""
		});
		return true;
	}

	function applySelection(key) {
		// Selection updates both controls and content panes so the static page behaves like the live site.
		const label = options.find(option => option.key === key)?.label || formatOptionLabel(key);
		for (const control of controls) {
			const labelEl = control.querySelector(".ng-value-label");
			if (labelEl) labelEl.textContent = label;
			const triggerLabel = control.querySelector(".mhtml-switch-trigger-label");
			if (triggerLabel) triggerLabel.textContent = label;

			const input = control.querySelector("input[role='combobox']");
			if (input) input.setAttribute("aria-expanded", "false");

			for (const item of control.querySelectorAll(".ng-option")) {
				const active = item.dataset.option === key;
				item.classList.toggle("selected", active);
				item.classList.toggle("marked", active);
			}
		}

		for (const view of views) {
			const option = readViewOption(view);
			const viewKey = option?.key ? viewKeyAlias.get(option.key) || option.key : "";
			view.hidden = !!viewKey && viewKey !== key;
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
		return String(value || "")
			.trim()
			.toLowerCase()
			.replace(/[^a-z0-9+#]+/g, "-")
			.replace(/^-+|-+$/g, "");
	}

	function cleanLabel(value) {
		return String(value || "").trim();
	}

	function formatOptionLabel(value) {
		const text = String(value || "")
			.trim()
			.replace(/^icon-/, "")
			.replace(/^-+|-+$/g, "")
			.replace(/[_-]+/g, " ")
			.replace(/\s+/g, " ")
			.trim();
		if (!text) return "";

		return text.split(" ").map(word => {
			if (!word) return word;
			if (/[^a-z]/i.test(word)) return word.toUpperCase();
			return word[0].toUpperCase() + word.slice(1);
		}).join(" ");
	}
})();
