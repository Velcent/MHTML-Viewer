/*
 * Restores before/after comparison sliders in static Epic documentation captures.
 */
(function () {
	if (window.__mhtmlEpicComparisonSliderReady) return;
	window.__mhtmlEpicComparisonSliderReady = true;

	injectStyle();
	setupComparisonSliders();

	function injectStyle() {
		if (document.getElementById("mhtml-epic-comparison-slider-style")) return;
		const style = document.createElement("style");
		style.id = "mhtml-epic-comparison-slider-style";
		style.textContent = `
			.block-comparison-slider.mhtml-comparison-ready {
				display: flex;
				position: relative;
				width: 100%;
				height: auto;
				overflow: hidden;
				max-width: 64rem;
				margin: auto;
				z-index: 1;
			}
			.block-comparison-slider.mhtml-comparison-ready .img-before {
				min-width: 100%;
				min-height: 100%;
				float: left;
				position: relative;
				transform: translate(-50%);
				overflow: hidden;
				background: #000;
			}
			.block-comparison-slider.mhtml-comparison-ready .img-before img {
				transform: translate(50%);
				width: 100% !important;
				height: 100%;
				object-fit: contain;
			}
			.block-comparison-slider.mhtml-comparison-ready .img-after {
				min-width: 100%;
				min-height: 100%;
				float: right;
				position: relative;
				transform: translate(-50%);
				overflow: hidden;
				background: #000;
			}
			.block-comparison-slider.mhtml-comparison-ready .img-after img {
				transform: translate(-50%);
				width: 100% !important;
				height: 100%;
				object-fit: contain;
			}
			.block-comparison-slider.mhtml-comparison-ready .slider {
				width: 100%;
				height: 100%;
				z-index: 5;
				position: absolute;
				inset: 0;
				-webkit-appearance: none;
				appearance: none;
				background: transparent;
				opacity: 0;
				cursor: ew-resize;
				pointer-events: none;
			}
			.block-comparison-slider.mhtml-comparison-ready .slider::-ms-track {
				width: 100%;
				cursor: pointer;
				background: transparent;
				border-color: transparent;
				color: transparent;
			}
			.block-comparison-slider.mhtml-comparison-ready .slider::-webkit-slider-thumb {
				background: transparent;
				border: 0;
				-webkit-appearance: none;
				width: 50px;
				height: 50px;
				cursor: ew-resize;
				transition: all .05s linear;
			}
			.block-comparison-slider.mhtml-comparison-ready .slider::-moz-range-thumb {
				background: transparent;
				border: 0;
				width: 50px;
				height: 50px;
				cursor: ew-resize;
				transition: all .05s linear;
			}
			.block-comparison-slider.mhtml-comparison-ready .slider-thumb {
				position: absolute;
				width: 100%;
				height: 100%;
				z-index: 4;
				transform: translate(50%);
				pointer-events: none;
			}
			.block-comparison-slider.mhtml-comparison-ready .slider-thumb-img {
				width: 20%;
				height: 100%;
				background-position: center;
				background-repeat: repeat-y;
				background-image:
					linear-gradient(45deg, transparent 45%, #111 46%, #111 54%, transparent 55%),
					linear-gradient(-45deg, transparent 45%, #111 46%, #111 54%, transparent 55%);
				transform: translate(-50%);
			}
			.block-comparison-slider.mhtml-comparison-ready .caption-before,
			.block-comparison-slider.mhtml-comparison-ready .caption-after {
				position: absolute;
				background-color: #000000b2;
				color: #fff;
				font-weight: 700;
				padding: .625rem;
				bottom: .625rem;
				max-width: 40%;
				border-radius: .375rem;
				z-index: 3;
			}
			.block-comparison-slider.mhtml-comparison-ready .caption-before {
				margin-left: .625rem;
			}
			.block-comparison-slider.mhtml-comparison-ready .caption-after {
				right: .625rem;
			}
			.block-comparison-slider.mhtml-comparison-ready .overlay-side {
				z-index: 2 !important;
			}
		`;
		document.head.appendChild(style);
	}

	function setupComparisonSliders() {
		const sliders = document.querySelectorAll(
			"block-comparison-slider, block-comparison-slider-md, .block-comparison-slider, [block-comparison-slider]"
		);
		for (const slider of sliders) setupSlider(slider);
	}

	function setupSlider(root) {
		// Captured markup may be incomplete, so missing controls/wrappers are created defensively.
		if (root.dataset.mhtmlComparisonReady === "true") return;
		const images = Array.from(root.querySelectorAll("img"));
		if (images.length < 2) return;

		root.dataset.mhtmlComparisonReady = "true";
		root.classList.add("block-comparison-slider", "mhtml-comparison-ready");

		const imgBefore = images[0];
		const imgAfter = images[1];
		const divBefore = ensureImageWrapper(root, imgBefore, "img-before");
		const divAfter = ensureImageWrapper(root, imgAfter, "img-after");
		const input = ensureInput(root);
		const thumb = ensureThumb(root);
		const captionBefore = ensureCaption(root, "caption-before", imgBefore.getAttribute("alt") || "");
		const captionAfter = ensureCaption(root, "caption-after", imgAfter.getAttribute("alt") || "");

		setAspectRatio(root, imgBefore, imgAfter);
		resolveImageSize(imgBefore).then(() => {
			setAspectRatio(root, imgBefore, imgAfter);
			update();
		});
		resolveImageSize(imgAfter).then(() => {
			setAspectRatio(root, imgBefore, imgAfter);
			update();
		});
		const update = () => applyValue(readSliderValue(input));

		input.addEventListener("input", update);
		input.addEventListener("change", update);
		window.addEventListener("resize", update, { passive: true });
		bindPointerDrag(root, input, applyValue);

		for (const image of [imgBefore, imgAfter]) {
			if (image.complete) continue;
			image.addEventListener("load", () => {
				setAspectRatio(root, imgBefore, imgAfter);
				update();
			}, { once: true });
		}

		update();

		function applyValue(value) {
			// Both image panes are translated from the same slider value to create the reveal effect.
			const amount = Math.max(0, Math.min(100, value));
			input.value = String(amount);
			imgBefore.style.setProperty("transform", `translateX(${100 - amount}%)`);
			divBefore.style.setProperty("transform", `translateX(${-1 * (100 - amount)}%)`);
			imgAfter.style.setProperty("transform", `translateX(${-1 * amount}%)`);
			divAfter.style.setProperty("transform", `translateX(${-1 * (100 - amount)}%)`);
			thumb.style.setProperty("transform", `translateX(${amount}%)`);

			if (amount < 50) {
				divBefore.classList.remove("overlay-side");
				divAfter.classList.add("overlay-side");
				captionBefore.classList.remove("overlay-side");
				captionAfter.classList.add("overlay-side");
			} else {
				divAfter.classList.remove("overlay-side");
				divBefore.classList.add("overlay-side");
				captionBefore.classList.add("overlay-side");
				captionAfter.classList.remove("overlay-side");
			}
		}
	}

	function bindPointerDrag(root, input, applyValue) {
		// Pointer events make the entire comparison area draggable, not just the hidden range input.
		let dragging = false;

		root.addEventListener("pointerdown", event => {
			if (event.button !== 0) return;
			dragging = true;
			root.setPointerCapture?.(event.pointerId);
			setFromPointer(event);
			event.preventDefault();
		});

		root.addEventListener("pointermove", event => {
			if (!dragging) return;
			setFromPointer(event);
			event.preventDefault();
		});

		root.addEventListener("pointerup", event => {
			if (!dragging) return;
			dragging = false;
			root.releasePointerCapture?.(event.pointerId);
			setFromPointer(event);
			event.preventDefault();
		});

		root.addEventListener("pointercancel", event => {
			dragging = false;
			root.releasePointerCapture?.(event.pointerId);
		});

		function setFromPointer(event) {
			const rect = root.getBoundingClientRect();
			if (rect.width <= 0) return;
			const value = ((event.clientX - rect.left) / rect.width) * 100;
			const clamped = Math.max(0, Math.min(100, value));
			input.value = String(clamped);
			applyValue(clamped);
		}
	}

	function ensureImageWrapper(root, image, className) {
		const parent = image.parentElement;
		if (parent && parent !== root) {
			parent.classList.add(className);
			return parent;
		}

		const wrapper = document.createElement("div");
		wrapper.className = className;
		root.insertBefore(wrapper, image);
		wrapper.appendChild(image);
		return wrapper;
	}

	function ensureInput(root) {
		let input = root.querySelector("input.slider[type='range']");
		if (input) return input;

		input = document.createElement("input");
		input.type = "range";
		input.min = "0";
		input.max = "100";
		input.value = "50";
		input.className = "slider";
		root.appendChild(input);
		return input;
	}

	function ensureThumb(root) {
		let thumb = root.querySelector(".slider-thumb");
		if (thumb) {
			if (!thumb.querySelector(".slider-thumb-img")) {
				const image = document.createElement("div");
				image.className = "slider-thumb-img";
				thumb.appendChild(image);
			}
			return thumb;
		}

		thumb = document.createElement("div");
		thumb.className = "slider-thumb";
		const image = document.createElement("div");
		image.className = "slider-thumb-img";
		thumb.appendChild(image);
		root.appendChild(thumb);
		return thumb;
	}

	function ensureCaption(root, className, text) {
		let caption = root.querySelector(`.${className}`);
		if (caption) return caption;

		caption = document.createElement("div");
		caption.className = className;
		caption.textContent = text;
		root.appendChild(caption);
		return caption;
	}

	function setAspectRatio(root, imgBefore, imgAfter) {
		const width = Number(imgBefore.getAttribute("width")) || imgBefore.naturalWidth || imgAfter.naturalWidth;
		const height = Number(imgBefore.getAttribute("height")) || imgBefore.naturalHeight || imgAfter.naturalHeight;
		if (width > 0 && height > 0) {
			root.style.aspectRatio = `${width} / ${height}`;
			root.style.height = "auto";
		}
	}

	function readSliderValue(input) {
		const value = input.valueAsNumber;
		if (Number.isFinite(value)) return value;
		const fallback = Number(input.value);
		return Number.isFinite(fallback) ? fallback : 50;
	}

	function resolveImageSize(image) {
		if (image.complete && image.naturalWidth > 0 && image.naturalHeight > 0) {
			return Promise.resolve();
		}
		if (typeof image.decode === "function") {
			return image.decode().catch(() => waitForImageLoad(image));
		}
		return waitForImageLoad(image);
	}

	function waitForImageLoad(image) {
		return new Promise(resolve => {
			if (image.complete) {
				resolve();
				return;
			}
			image.addEventListener("load", resolve, { once: true });
			image.addEventListener("error", resolve, { once: true });
		});
	}
})();
