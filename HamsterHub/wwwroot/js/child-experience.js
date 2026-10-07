(() => {
    'use strict';
    // Never autoplay speech, and stop it when leaving a screen or closing its dialog.
    document.querySelectorAll('[data-speak]').forEach(button => {
        button.addEventListener('click', () => {
            const language = document.documentElement.lang || 'ru';
            const showUnavailable = () => {
                let notice = button.parentElement.querySelector('[data-voice-error]');
                if (!notice) {
                    notice = document.createElement('p'); notice.dataset.voiceError = '';
                    notice.setAttribute('role', 'status'); button.after(notice);
                }
                notice.textContent = button.dataset.voiceUnavailable;
            };
            if (!('speechSynthesis' in window)) { showUnavailable(); return; }
            speechSynthesis.cancel();
            const words = new SpeechSynthesisUtterance(button.dataset.speak);
            words.lang = language; words.rate = 0.85;
            const voice = speechSynthesis.getVoices().find(item => item.lang.startsWith(language.slice(0, 2)));
            if (voice) words.voice = voice;
            words.onerror = event => { if (!['canceled', 'interrupted'].includes(event.error)) showUnavailable(); };
            speechSynthesis.speak(words);
        });
    });
    const stopSpeech = () => { if ('speechSynthesis' in window) speechSynthesis.cancel(); };
    document.addEventListener('hidden.bs.modal', stopSpeech);
    document.addEventListener('visibilitychange', () => { if (document.hidden) stopSpeech(); });
    window.addEventListener('pagehide', stopSpeech);

    document.querySelectorAll('[data-task-templates]').forEach(picker => {
        const form = picker.closest('form'); const prefix = picker.dataset.prefix;
        const visual = picker.querySelector('[data-visual-key]');
        const select = id => {
            visual.value = id;
            picker.querySelectorAll('[data-template]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.template === id)));
            visual.dispatchEvent(new Event('change', { bubbles: true }));
        };
        picker.querySelectorAll('[data-template]').forEach(button => button.addEventListener('click', () => {
            select(button.dataset.template);
            if (prefix === 'AddCareTask') {
                form.elements[`${prefix}.Name`].value = button.dataset.title;
                form.elements[`${prefix}.Instructions`].value = button.dataset.steps;
                form.elements[`${prefix}.Frequency`].value = button.dataset.frequency;
                form.elements[`${prefix}.Frequency`].dispatchEvent(new Event('change', { bubbles: true }));
            }
        }));
        picker.querySelector('[data-default-picture]').addEventListener('click', () => select(''));
    });

    const child = document.querySelector('[data-child-member]');
    if (child) {
        const key = `hamster-goal-${child.dataset.childMember}`;
        const summary = child.querySelector('[data-goal-summary]');
        const renderGoal = id => {
            const goal = [...child.querySelectorAll('[data-reward-goal]')].find(item => item.dataset.rewardGoal === id);
            summary.hidden = !goal;
            child.querySelectorAll('[data-reward-goal]').forEach(button => button.setAttribute('aria-pressed', String(button === goal)));
            if (!goal) return;
            summary.querySelector('[data-goal-title]').textContent = goal.dataset.goalName;
            summary.querySelector('progress')?.remove();
            summary.append(goal.closest('article').querySelector('progress').cloneNode(true));
        };
        try { renderGoal(localStorage.getItem(key)); } catch { /* Goal selection still works for this page. */ }
        child.querySelectorAll('[data-reward-goal]').forEach(button => button.addEventListener('click', () => {
            const id = button.dataset.rewardGoal;
            try { localStorage.setItem(key, id); } catch { }
            renderGoal(id); summary.scrollIntoView({ block: 'center', behavior: 'auto' });
        }));
    }
})();
