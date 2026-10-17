-- Runs inside the original GameModule environment, after its own source chunk.
local state = rawget(_G, '__eastward_language_patches_v1')
if not state then
    state = { languages = {}, styles = setmetatable({}, { __mode = 'k' }), styleLanguages = setmetatable({}, { __mode = 'k' }) }
    rawset(_G, '__eastward_language_patches_v1', state)
end

local runtime = {}
local function languages()
    local result = {}
    for code in pairs(state.languages) do result[#result + 1] = code end
    table.sort(result)
    return result
end

-- Inspect values, not decompiled local/upvalue names. Functions may close over
-- another helper function, as localeToName does in the original menus.
local function findTable(root, predicate)
    assert(debug and debug.getupvalue, 'Language patches require Lua debug.getupvalue')
    local seen = {}
    local function visit(value)
        if type(value) == 'table' then
            if predicate(value) then return value end
            return nil
        end
        if type(value) ~= 'function' or seen[value] then return nil end
        seen[value] = true
        for index = 1, math.huge do
            local name, upvalue = debug.getupvalue(value, index)
            if not name then break end
            local result = visit(upvalue)
            if result then return result end
        end
    end
    return visit(root)
end

local function requireTable(method, predicate, description)
    return assert(findTable(method, predicate), 'Unsupported language runtime: ' .. description .. ' table not found')
end

local function optionTable(value)
    local known = {}
    for _, entry in ipairs(value) do
        if type(entry) ~= 'table' or type(entry[1]) ~= 'string' or type(entry[2]) ~= 'string' then return false end
        known[entry[2]] = true
    end
    return known.en and known.default and known.ja
end

local function tagLanguageButton(button, code)
    runtime.installScopedStyles()
    state.widgetLanguages[button] = code
end

local function patchMenu(class, options)
    assert(class and class.initLocales, 'Language settings controller missing')
    local entries = class.__ewMenuEntries or requireTable(class.initLocales, optionTable, 'menu options')
    class.__ewMenuEntries = entries
    local found = false
    for _, entry in ipairs(entries) do
        if entry[2] == options.language then entry[1] = options.displayName; found = true; break end
    end
    if not found then entries[#entries + 1] = { options.displayName, options.language } end
    if class.__ewLanguageMenu then return end
    local init = class.initLocales
    class.initLocales = function(self, ...)
        local result = { init(self, ...) }
        for _, code in ipairs(languages()) do
            local button = assert(self:findChild('_LanguageButton_' .. code, true), 'Custom language menu button missing')
            tagLanguageButton(button, code)
        end
        return unpack(result)
    end
    class.__ewLanguageMenu = true
end

local function active()
    return state.languages[mock.getActiveLocale()]
end

local function patchFonts()
    if state.fontHook then return end
    assert(mock.TextStyle and mock.TextStyle.update, 'TextStyle.update missing')
    local update = mock.TextStyle.update
    mock.TextStyle.update = function(self, ...)
        state.styles[self] = true
        local originalFont = self.font
        local scoped = state.styleLanguages[self]
        local options = scoped and state.languages[scoped] or active()
        local selected = false
        if options and options.fontAsset then
            selected = scoped ~= nil or #options.originalFonts == 0
            for _, path in ipairs(options.originalFonts) do
                if originalFont == path then selected = true; break end
            end
        end
        if selected then self.font = options.fontAsset end
        local result = { pcall(update, self, ...) }
        self.font = originalFont
        if not result[1] then error(result[2], 0) end
        return unpack(result, 2)
    end
    mock.connectGlobalSignalFunc('locale.change', function()
        for style in pairs(state.styles) do
            local ok, reason = pcall(style.update, style)
            if not ok then print('[LanguagePatch] ' .. tostring(reason)) end
        end
    end)
    state.fontHook = true
end

local function patchImages()
    if state.imageHook then return end
    local enabled = false
    for _, options in pairs(state.languages) do
        if options.textureRemappings and next(options.textureRemappings) then enabled = true; break end
    end
    if not enabled then return end
    assert(mock.TexturePlane and mock.TexturePlane.setTexture and mock.UIImage and mock.UIImage.setImage,
        'Localized images require TexturePlane.setTexture and UIImage.setImage')
    state.texturePlanes = setmetatable({}, { __mode = 'k' })
    state.uiImages = setmetatable({}, { __mode = 'k' })
    local function mapped(path)
        local options = active()
        return options and options.textureRemappings and options.textureRemappings[path] or path
    end
    local function originalPath(path)
        -- A selector may already supply the generated localized texture path.
        for _, code in ipairs(languages()) do
            for original, replacement in pairs(state.languages[code].textureRemappings or {}) do
                if path == replacement then return original end
            end
        end
        return path
    end
    local function install(class, method, instances)
        local setter = class[method]
        class[method] = function(self, path, ...)
            local original = originalPath(path)
            instances[self] = { path = original }
            return setter(self, mapped(original), ...)
        end
    end
    install(mock.TexturePlane, 'setTexture', state.texturePlanes)
    install(mock.UIImage, 'setImage', state.uiImages)
    mock.connectGlobalSignalFunc('locale.change', function()
        for instance, record in pairs(state.texturePlanes) do
            local ok, reason = pcall(instance.setTexture, instance, record.path)
            if not ok then print('[LanguagePatch] ' .. tostring(reason)) end
        end
        for instance, record in pairs(state.uiImages) do
            local ok, reason = pcall(instance.setImage, instance, record.path)
            if not ok then print('[LanguagePatch] ' .. tostring(reason)) end
        end
    end)
    state.imageHook = true
end

local function patchLocaleFilters()
    if state.localeFilterHook then return end
    assert(mock.LocaleVisibility and mock.LocaleVisibility.update and mock.LocaleConditionalGroup
        and mock.LocaleConditionalGroup.__accept and mock.LocaleConditionalGroup.updateVisible,
        'Language fallback requires LocaleVisibility and LocaleConditionalGroup')
    state.localeVisibility = setmetatable({}, { __mode = 'k' })
    local alternatives = setmetatable({}, { __mode = 'k' })
    local function contains(list, value)
        for _, item in ipairs(list or {}) do if item == value then return true end end
        return false
    end
    -- Language variants share a parent in scene/proto data. Keep that context
    -- while conditions are evaluated, before load-only variants are discarded.
    local function collectAlternatives(data)
        local function visit(entities)
            local codes, filters = {}, {}
            local function add(id)
                local object = data.map[id]
                if object and (object.model == 'mock.LocaleConditionalGroup' or object.model == 'mock.LocaleVisibility') then
                    local body = object.body
                    filters[#filters + 1] = body
                    for _, code in ipairs(body.accept or {}) do
                        if code ~= 'all' then codes[code] = true end
                    end
                end
            end
            for _, entity in ipairs(entities or {}) do
                add(entity.id)
                for _, id in ipairs(entity.components or {}) do add(id) end
                visit(entity.children)
            end
            for _, body in ipairs(filters) do alternatives[body] = codes end
        end
        visit(data.entities)
    end
    if mock.SceneDeserializer then
        local calculate = mock.SceneDeserializer._calcConditionals
        local prepare = mock.SceneDeserializer._prepareEntitiesObjMap
        assert(calculate and prepare, 'Language fallback requires scene conditional deserialization')
        mock.SceneDeserializer._calcConditionals = function(self, data, ...)
            collectAlternatives(data)
            return calculate(self, data, ...)
        end
        mock.SceneDeserializer._prepareEntitiesObjMap = function(self, data, ...)
            local objects, ignored = prepare(self, data, ...)
            for id, object in pairs(data.map) do
                local entry = objects[id]
                if entry and alternatives[object.body] then alternatives[entry[1]] = alternatives[object.body] end
            end
            return objects, ignored
        end
    end
    local function fallback(data)
        local code = mock.getActiveLocale()
        local options = state.languages[code]
        local accept, exclude = data.accept, data.excluede
        if not options or contains(accept, code) or contains(exclude, code) then return accept, exclude end
        local variants = alternatives[data]
        if variants and variants[code] and not contains(accept, 'all') then
            local baseAccepted = contains(accept, options.baseLanguage)
                or (not next(accept or {}) and next(exclude or {}) and not contains(exclude, options.baseLanguage))
            if baseAccepted then return {}, { code } end
        end
        local accepted, excluded = {}, {}
        for _, item in ipairs(accept or {}) do accepted[#accepted + 1] = item end
        for _, item in ipairs(exclude or {}) do excluded[#excluded + 1] = item end
        if contains(accept, options.baseLanguage) then accepted[#accepted + 1] = code end
        if contains(exclude, options.baseLanguage) then excluded[#excluded + 1] = code end
        return accepted, excluded
    end
    local function wrap(class, method, tracked)
        local original = class[method]
        class[method] = function(self, ...)
            if tracked then state.localeVisibility[self] = true end
            local accept, exclude = self.accept, self.excluede
            self.accept, self.excluede = fallback(self)
            local result = { pcall(original, self, ...) }
            self.accept, self.excluede = accept, exclude
            if not result[1] then error(result[2], 0) end
            return unpack(result, 2)
        end
    end
    wrap(mock.LocaleVisibility, 'update', true)
    wrap(mock.LocaleConditionalGroup, 'updateVisible', false)
    local accept = mock.LocaleConditionalGroup.__accept
    mock.LocaleConditionalGroup.__accept = function(data, ...)
        local allowed, excluded = fallback(data)
        return accept(setmetatable({accept=allowed,excluede=excluded}, {__index=data}), ...)
    end
    mock.connectGlobalSignalFunc('locale.change', function()
        for component in pairs(state.localeVisibility) do
            local ok, reason = pcall(component.update, component)
            if not ok then print('[LanguagePatch] ' .. tostring(reason)) end
        end
    end)
    state.localeFilterHook = true
end

local function patchManager(class)
    assert(class and class.initLocale and class.setLocale, 'EWGlobalManager locale methods missing')
    patchFonts()
    patchImages()
    patchLocaleFilters()
    if class.__ewLanguageManager then return end
    local init = class.initLocale
    class.initLocale = function(self, ...)
        local manager = mock.getLocaleManager()
        for _, code in ipairs(languages()) do manager:affirmLocale(code) end
        return init(self, ...)
    end
    local setLocale = class.setLocale
    class.setLocale = function(self, code, ...)
        if state.languages[code] then mock.getLocaleManager():affirmLocale(code) end
        return setLocale(self, code, ...)
    end
    class.__ewLanguageManager = true
end

local function patchInitial(class)
    assert(class and class.onThread, 'Initial language controller missing')
    local selector = class.__ewInitialSelector or requireTable(class.onThread, function(value)
        return value.en == '_ButtonEN' and value.ja == '_ButtonJA'
    end, 'initial button selector')
    class.__ewInitialSelector = selector
    for _, code in ipairs(languages()) do
        if state.languages[code].includeInitialMenu then selector[code] = '_ButtonLanguage_' .. code end
    end
    if class.__ewLanguageInitial then return end
    local onThread = class.onThread
    class.onThread = function(self, ...)
        local template = assert(self:findChild('_ButtonEN', true), 'Initial language template button missing')
        local parent = assert(template:getParent(), 'Initial language button parent missing')
        for _, code in ipairs(languages()) do
            local options = state.languages[code]
            if options.includeInitialMenu then
                local name = '_ButtonLanguage_' .. code
                local button = self:findChild(name, true)
                if not button then
                    button = assert(mock.cloneEntity(template), 'Could not clone initial language button')
                    button:setName(name)
                    tagLanguageButton(button, code)
                    parent:addChild(button)
                end
                tagLanguageButton(button, code)
                button:setText(options.displayName)
                button.clicked:clearAndConnect(function()
                    getEWGlobalManager():setSetting('language', code)
                    return self:openEntry()
                end)
                local layout = parent.getLayout and parent:getLayout()
                if layout and options.firstRunButtonSpacing ~= nil then layout.spacing = options.firstRunButtonSpacing end
            end
        end
        local layout = parent.getLayout and parent:getLayout()
        if layout and parent.getChildren and parent.getSize then
            local count, minimum = 0, 0
            for child in pairs(parent:getChildren()) do
                count = count + 1
                local height = 40
                if child.getMinSizeHint then local _, hint = child:getMinSizeHint(); height = hint or height end
                minimum = minimum + height
            end
            local width, height = parent:getSize()
            local margin = layout.margin or { 0, 0, 0, 0 }
            local vertical = (margin[2] or 0) + (margin[4] or 0)
            if minimum + vertical > height then
                height = minimum + vertical
                parent:setSize(width, height)
            end
            if count > 1 then
                layout.spacing = math.min(layout.spacing or 0, math.max(0, (height - vertical - minimum) / (count - 1)))
            end
        end
        if parent.invalidateLayout then parent:invalidateLayout() end
        return onThread(self, ...)
    end
    class.__ewLanguageInitial = true
end

local function patchDialogStyles()
    if state.dialogStyleHook then return end
    assert(mock.TextLabel and mock.TextLabel.updateStyles and mock.StyleSheet and mock.TextStyle,
        'Scoped dialogue fonts require TextLabel and StyleSheet')
    state.dialogLabels = setmetatable({}, { __mode = 'k' })
    state.dialogSheets = setmetatable({}, { __mode = 'k' })
    state.widgetLanguages = setmetatable({}, { __mode = 'k' })
    state.dialogThreads = setmetatable({}, { __mode = 'k' })
    state.mainThread = {}
    local function cloneSheet(source, options)
        if not source then return nil end
        local cache = state.dialogSheets[source]
        if not cache then cache = {}; state.dialogSheets[source] = cache end
        local key = options.language .. ':' .. tostring(options.fontAsset)
        if cache[key] then return cache[key] end
        local sheet = mock.StyleSheet()
        for _, style in ipairs(source.styles) do
            local copy = mock.TextStyle()
            copy.name, copy.font, copy.size = style.name, style.font, style.size
            copy.fontRawSize, copy.allowScale = style.fontRawSize, style.allowScale
            copy.color = { unpack(style.color) }
            if options.fontAsset then copy.font = options.fontAsset end
            state.styleLanguages[copy] = options.language
            copy:update()
            sheet.styles[#sheet.styles + 1] = copy
        end
        cache[key] = sheet
        return sheet
    end
    local update = mock.TextLabel.updateStyles
    mock.TextLabel.updateStyles = function(self, ...)
        local controller = state.dialogLabels[self]
        local options = controller and state.languages[controller:getLocale()]
        if not options then return update(self, ...) end
        local source = not controller.useSourceStyle and options.styleSheetAsset and mock.loadAsset(options.styleSheetAsset) or self.styleSheet
        local sheet = cloneSheet(source, options)
        if not sheet then return update(self, ...) end
        local original = self.styleSheet
        self.styleSheet = sheet
        local result = { pcall(update, self, ...) }
        self.styleSheet = original
        if not result[1] then error(result[2], 0) end
        return unpack(result, 2)
    end
    assert(mock.UIWidgetElementText and mock.UIWidgetElementText.onUpdateStyle, 'Language button text renderer missing')
    local updateWidget = mock.UIWidgetElementText.onUpdateStyle
    mock.UIWidgetElementText.onUpdateStyle = function(self, widget, ...)
        local code = state.widgetLanguages[widget]
        if code and self.label then
            state.dialogLabels[self.label] = { getLocale = function() return code end, useSourceStyle = true }
        end
        return updateWidget(self, widget, ...)
    end
    local create = mock.createProtoInstance
    assert(create, 'Dialogue choice prototype factory missing')
    mock.createProtoInstance = function(path, ...)
        local result = create(path, ...)
        local controller = state.dialogThreads[coroutine.running() or state.mainThread]
        if controller and path == 'scene/design/ui/F_Dialog.scene/GP_DialogOption' and result then
            local label = result:findChildCom('_Text', 'TextLabel')
            if label then state.dialogLabels[label] = controller; label:updateStyles() end
        end
        return result
    end
    mock.connectGlobalSignalFunc('locale.change', function()
        for label in pairs(state.dialogLabels) do
            local ok, reason = pcall(label.updateStyles, label)
            if not ok then print('[LanguagePatch] ' .. tostring(reason)) end
        end
    end)
    state.dialogStyleHook = true
end

runtime.installScopedStyles = patchDialogStyles

local function patchDialog(class)
    assert(class and class.getLocaleClass and class.onStart, 'DialogBoxController methods missing')
    patchDialogStyles()
    if class.__ewLanguageDialog then return end
    local getClass = class.getLocaleClass
    class.getLocaleClass = function(self, ...)
        local options = state.languages[self:getLocale()]
        if options then
            return options.dialogClass or getClass(setmetatable({ getLocale = function() return options.baseLanguage end }, { __index = self }))
        end
        return getClass(self, ...)
    end
    local onStart = class.onStart
    class.onStart = function(self, ...)
        local options = state.languages[self:getLocale()]
        if options then
            local body = self:getTextBox()
            if body then state.dialogLabels[body] = self end
            if self.textBoxForLayout then state.dialogLabels[self.textBoxForLayout] = self end
            local title = self.getTitle and self:getTitle()
            if title then state.dialogLabels[title] = self end
        end
        local result = { onStart(self, ...) }
        local options = state.languages[self:getLocale()]
        if options then
            local textBox = self:getTextBox()
            local layoutBox = self.textBoxForLayout
            if options.styleSheetAsset then
                textBox:setStyleSheet(options.styleSheetAsset)
                layoutBox:setStyleSheet(options.styleSheetAsset)
            end
            if options.dialogLineSpacing ~= nil then
                textBox:setLineSpacing(options.dialogLineSpacing)
                layoutBox:setLineSpacing(options.dialogLineSpacing)
                self.lineSpacing = options.dialogLineSpacing
            end
            state.dialogLabels[textBox] = self
            state.dialogLabels[layoutBox] = self
            textBox:updateStyles()
            layoutBox:updateStyles()
            local title = self.getTitle and self:getTitle()
            if title then state.dialogLabels[title] = self; title:updateStyles() end
            if options.styleSheetAsset or options.fontAsset or options.dialogLineSpacing ~= nil then self:calcBestWidth() end
        end
        return unpack(result)
    end
    if class.doChoices then
        local choices = class.doChoices
        class.doChoices = function(self, ...)
            local thread = coroutine.running() or state.mainThread
            local previous = state.dialogThreads[thread]
            state.dialogThreads[thread] = self
            local result = { pcall(choices, self, ...) }
            state.dialogThreads[thread] = previous
            if not result[1] then error(result[2], 0) end
            return unpack(result, 2)
        end
    end
    class.__ewLanguageDialog = true
end

local function patchChapter(class, options)
    assert(class and class.actionChapterStart, 'ChapterTitleController missing')
    local mapping = class.__ewChapterMapping or requireTable(class.actionChapterStart, function(value)
        return value.en == 'EN' and value.ja == 'JA' and value['zh-CN'] == 'CN'
    end, 'chapter graphics selector')
    class.__ewChapterMapping = mapping
    mapping[options.language] = options.chapterGroup or mapping[options.baseLanguage] or mapping.en
end

local function patchTitle(class, options)
    assert(class and class.updateFarmMainTitle, 'DLC title controller missing')
    local mapping = class.__ewTitleMapping or requireTable(class.updateFarmMainTitle, function(value)
        return type(value.en) == 'string' and value.en:find('DLCTitle', 1, true)
            and type(value.ja) == 'string' and value.ja:find('DLCTitle', 1, true)
    end, 'DLC title texture selector')
    class.__ewTitleMapping = mapping
    mapping[options.language] = options.titleTexture or mapping[options.baseLanguage] or mapping.en
    if class.__ewTitleControllers then return end
    local instances = setmetatable({}, { __mode = 'k' })
    local update = class.updateFarmMainTitle
    class.updateFarmMainTitle = function(self, ...)
        instances[self] = true
        return update(self, ...)
    end
    mock.connectGlobalSignalFunc('locale.change', function()
        for instance in pairs(instances) do
            local ok, reason = pcall(instance.updateFarmMainTitle, instance)
            if not ok then print('[LanguagePatch] ' .. tostring(reason)) end
        end
    end)
    class.__ewTitleControllers = instances
end

function runtime.apply(kind, options, environment)
    assert(type(options.language) == 'string' and type(options.displayName) == 'string', 'Invalid language patch options')
    state.languages[options.language] = options
    if kind == 'manager' then patchManager(environment.EWGlobalManager)
    elseif kind == 'settings' then patchMenu(environment.SettingMenuController, options)
    elseif kind == 'farm-settings' then patchMenu(environment.FarmSettingMenuController, options)
    elseif kind == 'initial' then patchInitial(environment.LanguageSelectionController)
    elseif kind == 'dialog' then patchDialog(environment.DialogBoxController)
    elseif kind == 'chapter' then patchChapter(environment.ChapterTitleController, options)
    elseif kind == 'title' then patchTitle(environment.TitleMenuController, options)
    else error('Unknown language patch module: ' .. tostring(kind)) end
end

return runtime
