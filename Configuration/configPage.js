define(['loading', 'emby-button', 'emby-checkbox'], function (loading) {
    'use strict';

    var pluginId = '9b7dca24-edf8-4155-bbd6-8c2b0f07448d';

    function readValue(source, pascalName, camelName, fallback) {
        if (source && typeof source[pascalName] !== 'undefined') {
            return source[pascalName];
        }

        if (source && typeof source[camelName] !== 'undefined') {
            return source[camelName];
        }

        return fallback;
    }

    function parseJson(value) {
        if (typeof value === 'string') {
            return JSON.parse(value);
        }

        return value;
    }

    function requestJson(path, query) {
        var url = ApiClient.getUrl(path);
        var queryParts = [];
        if (query) {
            for (var key in query) {
                if (Object.prototype.hasOwnProperty.call(query, key)) {
                    queryParts.push(encodeURIComponent(key) + '=' + encodeURIComponent(query[key]));
                }
            }
        }

        if (queryParts.length) {
            url += (url.indexOf('?') === -1 ? '?' : '&') + queryParts.join('&');
        }

        if (typeof ApiClient.getJSON === 'function') {
            return ApiClient.getJSON(url).then(parseJson);
        }

        return ApiClient.ajax({
            type: 'GET',
            url: url,
            dataType: 'json'
        }).then(parseJson);
    }

    function initializePage(page) {
        if (!page || page.getAttribute('data-thundercat-bound') === 'true') {
            return;
        }

        var form = page.querySelector('.thunderCatConfigurationForm');
        var subtitleCatCheckbox = page.querySelector('#enableSubtitleCat');
        var thunderCheckbox = page.querySelector('#enableThunder');
        var saveButton = page.querySelector('[data-role="save-button"]');
        var saveStatus = page.querySelector('[data-role="save-status"]');
        var diagnosticStatus = page.querySelector('[data-role="diagnostic-status"]');
        if (!form || !subtitleCatCheckbox || !thunderCheckbox || !saveButton || !saveStatus) {
            return;
        }

        page.setAttribute('data-thundercat-bound', 'true');

        var runtimeInfo = {
            PluginVersion: '—',
            ServerVersion: '—',
            CompatibilityBaseline: '—'
        };
        var providerHealth = {
            SubtitleCat: '未测试',
            Thunder: '未测试'
        };
        var savedState = null;
        var readyAttempts = 0;
        var testButtons = page.querySelectorAll('[data-action="test-provider"]');

        function setProviderButtonsDisabled(disabled) {
            for (var index = 0; index < testButtons.length; index++) {
                testButtons[index].disabled = disabled;
            }
        }

        function currentState() {
            return (subtitleCatCheckbox.checked ? '1' : '0') + ':' +
                (thunderCheckbox.checked ? '1' : '0');
        }

        function setSaveStatus(state, value) {
            saveStatus.setAttribute('data-state', state);
            saveStatus.textContent = value;
        }

        function refreshDirtyState() {
            if (savedState === null) {
                saveButton.disabled = true;
                return;
            }

            var isDirty = currentState() !== savedState;
            saveButton.disabled = !isDirty;
            setSaveStatus(isDirty ? 'dirty' : 'ok', isDirty ? '有未保存的更改' : '配置已保存');
        }

        function setText(role, value) {
            var elements = page.querySelectorAll('[data-role="' + role + '"]');
            for (var index = 0; index < elements.length; index++) {
                elements[index].textContent = value;
            }
        }

        function loadRuntimeInfo() {
            return requestJson('ThunderCat/Info').then(function (info) {
                runtimeInfo.PluginVersion = readValue(info, 'PluginVersion', 'pluginVersion', '—');
                runtimeInfo.ServerVersion = readValue(info, 'ServerVersion', 'serverVersion', '—');
                runtimeInfo.CompatibilityBaseline = readValue(
                    info,
                    'CompatibilityBaseline',
                    'compatibilityBaseline',
                    '—');
                setText('plugin-version', runtimeInfo.PluginVersion);
                setText('runtime-plugin-version', runtimeInfo.PluginVersion);
                setText('runtime-server-version', runtimeInfo.ServerVersion);
                setText('runtime-baseline', runtimeInfo.CompatibilityBaseline);
            }).catch(function (error) {
                console.error('[ThunderCat] Failed to load runtime information.', error);
                setText('runtime-plugin-version', '读取失败');
                setText('runtime-server-version', '读取失败');
                setText('runtime-baseline', '读取失败');
            });
        }

        function loadConfiguration() {
            if (typeof ApiClient === 'undefined') {
                if (readyAttempts++ < 40) {
                    window.setTimeout(loadConfiguration, 100);
                } else {
                    setSaveStatus('error', 'Emby API 未就绪，请刷新页面');
                }
                return;
            }

            readyAttempts = 0;
            loading.show();
            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                subtitleCatCheckbox.checked = readValue(
                    config,
                    'EnableSubtitleCat',
                    'enableSubtitleCat',
                    true) === true;
                thunderCheckbox.checked = readValue(
                    config,
                    'EnableThunder',
                    'enableThunder',
                    true) === true;
                savedState = currentState();
                setProviderButtonsDisabled(false);
                refreshDirtyState();
                return loadRuntimeInfo();
            }).then(function () {
                loading.hide();
            }).catch(function (error) {
                console.error('[ThunderCat] Failed to load plugin configuration.', error);
                setProviderButtonsDisabled(false);
                setSaveStatus('error', '配置读取失败，请查看 Emby 日志');
                loading.hide();
            });
        }

        function updateProviderStatus(provider, state, value) {
            var card = page.querySelector('[data-provider-card="' + provider + '"]');
            var status = card ? card.querySelector('[data-role="provider-status"]') : null;
            if (!status) {
                return;
            }

            status.setAttribute('data-state', state);
            status.textContent = value;
            providerHealth[provider] = value;
        }

        function testProvider(button) {
            var provider = button.getAttribute('data-provider');
            button.disabled = true;
            updateProviderStatus(provider, 'testing', '正在测试…');
            requestJson('ThunderCat/Health', { Provider: provider }).then(function (result) {
                var available = readValue(result, 'IsAvailable', 'isAvailable', false) === true;
                var status = readValue(result, 'Status', 'status', 'unavailable');
                var elapsed = readValue(result, 'ElapsedMilliseconds', 'elapsedMilliseconds', 0);
                if (available) {
                    updateProviderStatus(provider, 'ok', '可用 · ' + elapsed + ' ms');
                } else if (status === 'timeout') {
                    updateProviderStatus(provider, 'error', '连接超时');
                } else {
                    updateProviderStatus(provider, 'error', '暂不可用');
                }
            }).catch(function (error) {
                console.error('[ThunderCat] Provider health check failed.', error);
                updateProviderStatus(provider, 'error', '测试失败');
            }).then(function () {
                button.disabled = false;
            });
        }

        function diagnosticText() {
            return [
                'ThunderCat diagnostics',
                'Plugin: ' + runtimeInfo.PluginVersion,
                'Emby Server: ' + runtimeInfo.ServerVersion,
                'Compatibility baseline: ' + runtimeInfo.CompatibilityBaseline,
                'SubtitleCat: ' + (subtitleCatCheckbox.checked ? 'enabled' : 'disabled') +
                    ' / ' + providerHealth.SubtitleCat,
                'Thunder: ' + (thunderCheckbox.checked ? 'enabled' : 'disabled') +
                    ' / ' + providerHealth.Thunder
            ].join('\n');
        }

        function copyText(value) {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                return navigator.clipboard.writeText(value);
            }

            return new Promise(function (resolve, reject) {
                var textarea = document.createElement('textarea');
                textarea.value = value;
                textarea.setAttribute('readonly', 'readonly');
                textarea.style.position = 'fixed';
                textarea.style.opacity = '0';
                document.body.appendChild(textarea);
                textarea.select();
                try {
                    if (document.execCommand('copy')) {
                        resolve();
                    } else {
                        reject(new Error('Copy command was rejected.'));
                    }
                } catch (error) {
                    reject(error);
                } finally {
                    document.body.removeChild(textarea);
                }
            });
        }

        subtitleCatCheckbox.addEventListener('change', refreshDirtyState);
        thunderCheckbox.addEventListener('change', refreshDirtyState);

        for (var testIndex = 0; testIndex < testButtons.length; testIndex++) {
            testButtons[testIndex].disabled = true;
            testButtons[testIndex].addEventListener('click', function () {
                testProvider(this);
            });
        }

        var copyButton = page.querySelector('[data-action="copy-diagnostics"]');
        if (copyButton) {
            copyButton.addEventListener('click', function () {
                copyText(diagnosticText()).then(function () {
                    diagnosticStatus.textContent = '诊断信息已复制。';
                }).catch(function (error) {
                    console.error('[ThunderCat] Failed to copy diagnostics.', error);
                    diagnosticStatus.textContent = '复制失败，请检查浏览器权限。';
                });
            });
        }

        form.addEventListener('submit', function (event) {
            event.preventDefault();
            saveButton.disabled = true;
            setSaveStatus('testing', '正在保存…');
            loading.show();
            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                config.EnableSubtitleCat = subtitleCatCheckbox.checked;
                config.EnableThunder = thunderCheckbox.checked;
                return ApiClient.updatePluginConfiguration(pluginId, config);
            }).then(function (result) {
                savedState = currentState();
                refreshDirtyState();
                loading.hide();
                if (typeof Dashboard !== 'undefined' &&
                    typeof Dashboard.processPluginConfigurationUpdateResult === 'function') {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                }
            }).catch(function (error) {
                console.error('[ThunderCat] Failed to save plugin configuration.', error);
                saveButton.disabled = false;
                setSaveStatus('error', '保存失败，请查看 Emby 日志');
                loading.hide();
            });
            return false;
        });

        page.addEventListener('viewshow', loadConfiguration);
    }

    return function (view) {
        initializePage(view);
    };
});
