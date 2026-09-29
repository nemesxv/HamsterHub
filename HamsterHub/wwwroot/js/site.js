document.addEventListener("DOMContentLoaded", () => {
  const themeButton = document.querySelector("[data-theme-toggle]");
  themeButton?.addEventListener("click", () => {
    const nextTheme = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
    document.documentElement.dataset.theme = nextTheme;
    localStorage.setItem("hamsterhub-theme", nextTheme);
  });

  const openDialog = window.hamsterHubDialog;
  const modalId = openDialog === "login"
    ? "loginModal"
    : openDialog === "signup"
      ? "signupModal"
      : null;

  if (modalId) {
    const modalElement = document.getElementById(modalId);
    if (modalElement) {
      bootstrap.Modal.getOrCreateInstance(modalElement).show();
    }
  }

  document.querySelectorAll("[data-switch-modal]").forEach((button) => {
    button.addEventListener("click", () => {
      const currentModal = button.closest(".modal");
      const nextModal = document.getElementById(button.dataset.switchModal);

      if (!currentModal || !nextModal) {
        return;
      }

      const currentInstance = bootstrap.Modal.getOrCreateInstance(currentModal);
      currentModal.addEventListener("hidden.bs.modal", () => {
        bootstrap.Modal.getOrCreateInstance(nextModal).show();
      }, { once: true });
      currentInstance.hide();
    });
  });

  document.querySelectorAll("form[data-confirm]").forEach((form) => {
    form.addEventListener("submit", (event) => {
      if (!window.confirm(form.dataset.confirm)) {
        event.preventDefault();
      }
    });
  });

  const dashboardPage = document.querySelector(".dashboard-shell, .kid-canvas");
  const dashboardScrollKey = "hamsterHub.dashboardScroll";

  if (dashboardPage) {
    try {
      const savedScroll = JSON.parse(sessionStorage.getItem(dashboardScrollKey));
      sessionStorage.removeItem(dashboardScrollKey);

      if (savedScroll?.path === window.location.pathname &&
          Number.isFinite(savedScroll.top) &&
          Date.now() - savedScroll.savedAt < 60000) {
        requestAnimationFrame(() => {
          requestAnimationFrame(() => {
            window.scrollTo({ top: savedScroll.top, left: 0, behavior: "auto" });
          });
        });
      }
    } catch {
      // Continue normally when browser storage is unavailable or malformed.
    }

    document.querySelectorAll("form").forEach((form) => {
      form.addEventListener("submit", (event) => {
        if (event.defaultPrevented) {
          return;
        }

        try {
          sessionStorage.setItem(dashboardScrollKey, JSON.stringify({
            path: window.location.pathname,
            top: window.scrollY,
            savedAt: Date.now()
          }));
        } catch {
          // The action should still submit if browser storage is unavailable.
        }
      });
    });
  } else {
    try {
      sessionStorage.removeItem(dashboardScrollKey);
    } catch {
      // Browser storage may be unavailable in a restricted context.
    }
  }

  document.querySelectorAll("[data-dashboard-notice]").forEach((notice) => {
    let dismissTimer;
    let isDismissed = false;

    const dismissNotice = () => {
      if (isDismissed) {
        return;
      }

      isDismissed = true;
      window.clearTimeout(dismissTimer);
      notice.classList.add("is-leaving");
      window.setTimeout(() => notice.remove(), 220);
    };
    const scheduleDismissal = () => {
      if (isDismissed) {
        return;
      }

      window.clearTimeout(dismissTimer);
      dismissTimer = window.setTimeout(dismissNotice, 6000);
    };

    notice.addEventListener("click", dismissNotice);
    notice.querySelector("[data-dashboard-notice-close]")
      ?.addEventListener("click", (event) => {
        event.stopPropagation();
        dismissNotice();
      });
    notice.addEventListener("pointerenter", () => window.clearTimeout(dismissTimer));
    notice.addEventListener("pointerleave", scheduleDismissal);
    notice.addEventListener("focusin", () => window.clearTimeout(dismissTimer));
    notice.addEventListener("focusout", scheduleDismissal);
    scheduleDismissal();
  });

  const photoViewer = document.querySelector("[data-photo-viewer]");
  const photoViewerImage = photoViewer?.querySelector("[data-photo-viewer-image]");
  const photoViewerCaption = photoViewer?.querySelector("[data-photo-viewer-caption]");
  const photoViewerCount = photoViewer?.querySelector("[data-photo-viewer-count]");
  const previousPhotoButton = photoViewer?.querySelector("[data-photo-viewer-previous]");
  const nextPhotoButton = photoViewer?.querySelector("[data-photo-viewer-next]");
  let viewerPhotos = [];
  let viewerIndex = 0;
  let viewerTrigger = null;

  const renderViewerPhoto = () => {
    if (!photoViewerImage || viewerPhotos.length === 0) {
      return;
    }

    const photo = viewerPhotos[viewerIndex];
    photoViewerImage.src = photo.src;
    photoViewerImage.alt = photo.alt;
    if (photoViewerCaption) {
      photoViewerCaption.textContent = photo.alt;
    }
    if (photoViewerCount) {
      photoViewerCount.textContent = viewerPhotos.length > 1
        ? `${viewerIndex + 1} / ${viewerPhotos.length}`
        : "";
    }
    previousPhotoButton?.toggleAttribute("hidden", viewerPhotos.length < 2);
    nextPhotoButton?.toggleAttribute("hidden", viewerPhotos.length < 2);
  };

  const openPhotoViewer = (photos, trigger) => {
    if (!photoViewer || photos.length === 0) {
      return;
    }

    viewerPhotos = photos;
    viewerIndex = 0;
    viewerTrigger = trigger;
    renderViewerPhoto();
    photoViewer.hidden = false;
    document.body.classList.add("photo-viewer-open");
    photoViewer.querySelector("[data-photo-viewer-close]")?.focus();
  };

  const closePhotoViewer = () => {
    if (!photoViewer || photoViewer.hidden) {
      return;
    }

    photoViewer.hidden = true;
    document.body.classList.remove("photo-viewer-open");
    photoViewerImage?.removeAttribute("src");
    viewerTrigger?.focus();
    viewerTrigger = null;
    viewerPhotos = [];
  };

  document.querySelectorAll("[data-photo-viewer-src]").forEach((trigger) => {
    trigger.addEventListener("click", () => {
      openPhotoViewer([{
        src: trigger.dataset.photoViewerSrc,
        alt: trigger.dataset.photoViewerAlt || ""
      }], trigger);
    });
  });

  document.querySelectorAll("[data-photo-gallery]").forEach((trigger) => {
    trigger.addEventListener("click", () => {
      const photos = Array.from(trigger.querySelectorAll("[data-gallery-photo]"))
        .map((item) => ({
          src: item.dataset.src,
          alt: item.dataset.alt || ""
        }))
        .filter((item) => item.src);
      openPhotoViewer(photos, trigger);
    });
  });

  photoViewer?.querySelector("[data-photo-viewer-close]")
    ?.addEventListener("click", closePhotoViewer);
  photoViewer?.addEventListener("click", (event) => {
    if (event.target === photoViewer) {
      closePhotoViewer();
    }
  });
  previousPhotoButton?.addEventListener("click", () => {
    viewerIndex = (viewerIndex - 1 + viewerPhotos.length) % viewerPhotos.length;
    renderViewerPhoto();
  });
  nextPhotoButton?.addEventListener("click", () => {
    viewerIndex = (viewerIndex + 1) % viewerPhotos.length;
    renderViewerPhoto();
  });
  document.addEventListener("keydown", (event) => {
    if (!photoViewer || photoViewer.hidden) {
      return;
    }

    if (event.key === "Escape") {
      closePhotoViewer();
    } else if (event.key === "ArrowLeft" && viewerPhotos.length > 1) {
      previousPhotoButton?.click();
    } else if (event.key === "ArrowRight" && viewerPhotos.length > 1) {
      nextPhotoButton?.click();
    }
  });

  document.querySelectorAll(".kid-complete-modal").forEach((modal) => {
    const form = modal.querySelector("form");
    const cameraOpenButton = modal.querySelector("[data-camera-open]");
    const cameraInput = modal.querySelector("[data-camera-input]");
    const galleryInput = modal.querySelector("[data-gallery-input]");
    const cameraPanel = modal.querySelector("[data-camera-panel]");
    const cameraVideo = modal.querySelector("[data-camera-video]");
    const cameraCanvas = modal.querySelector("[data-camera-canvas]");
    const cameraCaptureButton = modal.querySelector("[data-camera-capture]");
    const cameraCancelButton = modal.querySelector("[data-camera-cancel]");
    const preview = modal.querySelector("[data-photo-preview]");
    let cameraStream = null;
    let previewUrls = [];
    let selectedFiles = [];

    const stopCamera = () => {
      cameraStream?.getTracks().forEach((track) => track.stop());
      cameraStream = null;
      if (cameraVideo) {
        cameraVideo.srcObject = null;
      }
      if (cameraPanel) {
        cameraPanel.hidden = true;
      }
    };

    const renderSelectedPhotos = () => {
      previewUrls.forEach((url) => URL.revokeObjectURL(url));
      previewUrls = [];
      if (!preview) {
        return;
      }

      preview.replaceChildren();
      selectedFiles.forEach((file, index) => {
        const item = document.createElement("div");
        const image = document.createElement("img");
        const url = URL.createObjectURL(file);
        previewUrls.push(url);
        image.src = url;
        image.alt = file.name;
        const remove = document.createElement("button");
        remove.type = "button";
        remove.textContent = modal.dataset.photoRemove;
        remove.addEventListener("click", () => {
          selectedFiles.splice(index, 1);
          syncPhotos();
        });
        item.append(image, remove);
        preview.appendChild(item);
      });
    };
    const syncPhotos = () => {
      const transfer = new DataTransfer();
      selectedFiles.forEach(file => transfer.items.add(file));
      galleryInput.files = transfer.files;
      cameraInput.value = "";
      renderSelectedPhotos();
    };
    const addPhotos = (files) => {
      if (selectedFiles.length + files.length > 8) {
        window.alert(modal.dataset.photoLimit);
        syncPhotos();
        return;
      }
      selectedFiles.push(...files);
      syncPhotos();
    };

    cameraOpenButton?.addEventListener("click", async () => {
      if (!navigator.mediaDevices?.getUserMedia || !cameraVideo || !cameraPanel) {
        cameraInput?.click();
        return;
      }

      try {
        cameraStream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: { ideal: "environment" } },
          audio: false
        });
        cameraVideo.srcObject = cameraStream;
        cameraPanel.hidden = false;
        await cameraVideo.play();
      } catch {
        cameraInput?.click();
      }
    });

    cameraCaptureButton?.addEventListener("click", () => {
      if (!cameraVideo || !cameraCanvas || !cameraInput ||
          cameraVideo.videoWidth === 0 || cameraVideo.videoHeight === 0) {
        return;
      }

      cameraCanvas.width = cameraVideo.videoWidth;
      cameraCanvas.height = cameraVideo.videoHeight;
      cameraCanvas.getContext("2d")?.drawImage(
        cameraVideo, 0, 0, cameraCanvas.width, cameraCanvas.height);
      cameraCanvas.toBlob((blob) => {
        if (!blob) {
          return;
        }

        addPhotos([new File([blob], `hamsterhub-${Date.now()}.jpg`, { type: "image/jpeg" })]);
        stopCamera();
      }, "image/jpeg", 0.9);
    });

    cameraCancelButton?.addEventListener("click", stopCamera);
    cameraInput?.addEventListener("change", () => addPhotos(Array.from(cameraInput.files || [])));
    galleryInput?.addEventListener("click", () => { galleryInput.value = ""; });
    galleryInput?.addEventListener("change", () => addPhotos(Array.from(galleryInput.files || [])));
    galleryInput?.addEventListener("cancel", syncPhotos);
    form?.addEventListener("submit", syncPhotos);
    modal.addEventListener("hidden.bs.modal", () => {
      stopCamera();
      form?.reset();
      selectedFiles = [];
      syncPhotos();
    });
  });

  document.querySelectorAll("[data-direct-reward-form]").forEach((form) => {
    const rewardSelect = form.querySelector("[data-direct-reward-reward]");
    const childSelect = form.querySelector("[data-direct-reward-child]");
    const costOutput = form.querySelector("[data-direct-reward-cost]");
    const balanceOutput = form.querySelector("[data-direct-reward-balance]");
    const remainingOutput = form.querySelector("[data-direct-reward-remaining]");
    const summary = form.querySelector("[data-direct-reward-summary]");
    const warning = form.querySelector("[data-direct-reward-warning]");
    const submitButton = form.querySelector("[data-direct-reward-submit]");

    const renderRewardSummary = () => {
      const rewardOption = rewardSelect?.selectedOptions[0];
      const childOption = childSelect?.selectedOptions[0];
      const hasReward = Boolean(rewardSelect?.value && rewardOption?.dataset.pointsCost);
      const hasChild = Boolean(childSelect?.value && childOption?.dataset.currentPoints);
      const cost = hasReward ? Number(rewardOption.dataset.pointsCost) : null;
      const balance = hasChild ? Number(childOption.dataset.currentPoints) : null;
      const remaining = cost !== null && balance !== null ? balance - cost : null;
      const isInsufficient = remaining !== null && remaining < 0;

      if (costOutput) {
        costOutput.textContent = cost ?? "—";
      }
      if (balanceOutput) {
        balanceOutput.textContent = balance ?? "—";
      }
      if (remainingOutput) {
        remainingOutput.textContent = remaining ?? "—";
      }
      summary?.classList.toggle("is-insufficient", isInsufficient);
      if (warning) {
        warning.hidden = !isInsufficient;
      }
      if (submitButton) {
        submitButton.disabled = !hasReward || !hasChild || isInsufficient;
      }
    };

    rewardSelect?.addEventListener("change", renderRewardSummary);
    childSelect?.addEventListener("change", renderRewardSummary);
    renderRewardSummary();
  });
});

(() => {
  const labels = document.querySelector("[data-photo-labels]")?.dataset;
  if (!labels) return;
  document.querySelectorAll('input[type="file"][accept*="image"]').forEach(input => {
    if (input.closest(".kid-complete-modal")) return;
    const limit = input.multiple ? 8 : 1;
    const picker = input.cloneNode();
    picker.removeAttribute("name"); picker.removeAttribute("id");
    picker.hidden = true;
    const camera = document.createElement("input");
    camera.type = "file"; camera.accept = input.accept;
    camera.setAttribute("capture", "environment"); camera.hidden = true;
    const controls = document.createElement("div"); controls.className = "image-source-controls";
    const preview = document.createElement("div"); preview.className = "image-source-preview";
    let files = [], urls = [];
    function render() {
      const transfer = new DataTransfer(); files.forEach(file => transfer.items.add(file));
      input.files = transfer.files;
      urls.forEach(url => URL.revokeObjectURL(url)); urls = [];
      preview.replaceChildren();
      files.forEach((file, index) => {
        const item = document.createElement("div"), image = document.createElement("img");
        image.src = URL.createObjectURL(file); urls.push(image.src); image.alt = file.name;
        const remove = document.createElement("button"); remove.type = "button";
        remove.textContent = labels.remove;
        remove.addEventListener("click", () => { files.splice(index, 1); render(); });
        item.append(image, remove); preview.append(item);
      });
    }
    function accept(source) {
      const chosen = Array.from(source.files || []); source.value = "";
      if (!chosen.length) return;
      const next = limit === 1 ? chosen : [...files, ...chosen];
      if (next.length > limit) { window.alert(labels.limit); return; }
      files = next; render();
    }
    [[labels.camera, camera], [labels.gallery, picker]].forEach(([label, source]) => {
      const button = document.createElement("button"); button.type = "button";
      button.textContent = label; button.addEventListener("click", () => source.click());
      source.addEventListener("change", () => accept(source)); controls.append(button);
    });
    input.hidden = true; input.after(controls, picker, camera, preview);
    input.form?.addEventListener("reset", () => { files = []; render(); });
  });
})();
