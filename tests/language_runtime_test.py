"""Optional LuaJIT host check (Lupa) for the embedded language patch runtime.
python language_runtime_test.py [directory of actual-archive generated Lua patches]
The engine is mocked; no game executable or game installation writes occur.
"""
from pathlib import Path
import sys
from lupa.luajit21 import LuaRuntime

lua = LuaRuntime(unpack_returned_tuples=True)
lua.execute(r"""
local locale = 'en'
local registered, callbacks = {}, {}
local manager = {
    affirmLocale = function(self, code) registered[code] = true end,
    setActiveLocale = function(self, code) assert(registered[code]); locale = code end
}
local settings = { language = 'ko', dialog_language = 'en' }
local ew = { defaultLocale = 'en', getSetting = function(self,key) return settings[key] end,
    setSetting = function(self,key,value) settings[key] = value end }
function getEWGlobalManager() return ew end
mock = {
    getActiveLocale = function() return locale end,
    getLocaleManager = function() return manager end,
    connectGlobalSignalFunc = function(name, callback)
        callbacks[name] = callbacks[name] or {}; table.insert(callbacks[name],callback)
    end
}
function switch(code)
    locale = code
    for _, callback in ipairs(callbacks['locale.change'] or {}) do callback(code) end
end
local function filter(data)
    local allow = false
    for _, value in ipairs(data.accept) do if value=='all' or value==locale then allow=true end end
    if not allow and next(data.excluede) then
        allow=true;for _,value in ipairs(data.excluede) do if value==locale then allow=false end end
    end
    return allow
end
mock.LocaleVisibility={update=function(self)self.visible=filter(self)end}
mock.LocaleConditionalGroup={updateVisible=function(self)self.visible=filter(self)end,
    __accept=function(data)if data.mode=='show' then return true end;return filter(data)end}
mock.TextStyle = setmetatable({update = function(self)
    if self.fail then error('font failure') end
    self.renderedFont = self.font
end}, {__call=function(class) return setmetatable({}, {__index=class}) end})
mock.StyleSheet = setmetatable({applyToTextBox = function(self, box)
    box.renderedStyles = self.styles
end}, {__call=function(class) return setmetatable({styles={}}, {__index=class}) end})
local sourceStyle = setmetatable({name='default',font='font/base.ttf',size=19,fontRawSize=24,
    color={.1,.2,.3,.4},allowScale=true}, {__index=mock.TextStyle})
local sourceSheet = setmetatable({styles={sourceStyle}}, {__index=mock.StyleSheet})
mock.loadAsset = function() return sourceSheet end
mock.TextLabel = {
    updateStyles=function(self) self.styleSheet:applyToTextBox(self.box) end,
    setStyleSheet=function(self, path) self.styleSheetPath=path;self.styleSheet=mock.loadAsset(path);self:updateStyles() end,
    setLineSpacing=function(self, value) self.lineSpacing=value end
}
function label() return setmetatable({styleSheet=sourceSheet,box={}}, {__index=mock.TextLabel}) end
mock.UIWidgetElementText = {onUpdateStyle=function(self,widget)self.label:setStyleSheet('font/original.stylesheet')end}
mock.TexturePlane = {setTexture=function(self,path) self.texture=path end}
mock.UIImage = {setImage=function(self,path) self.image=path end}
mock.createProtoInstance = function(path)
    local text = label()
    return {label=text,findChildCom=function(self) return self.label end}
end
EWGlobalManager = {
    initLocale=function(self) assert(registered.ko); manager:setActiveLocale('ko') end,
    setLocale=function(self,code) manager:setActiveLocale(code) end
}
local function menu()
    local totallyRenamedEntries = {{'Default','default'},{'English','en'},{'Japanese','ja'}}
    local function arbitraryName(id)
        for _, entry in ipairs(totallyRenamedEntries) do if entry[2]==id then return entry[1] end end
    end
    return {
        initLocales=function(self) self.entries=totallyRenamedEntries end,
        findChild=function(self,name)self.buttons=self.buttons or {};self.buttons[name]=self.buttons[name] or {};return self.buttons[name] end,
        updateLanguageButtons=function(self) self.display=arbitraryName(settings.language) end,
        openLanguageOptions=function(self) self.focus=arbitraryName(settings.language) and settings.language or 'default' end
    }
end
SettingMenuController, FarmSettingMenuController = menu(), menu()
local buttons = {}
local parent = {layout={spacing=14,margin={10,10,10,10}},getSize=function()return 240,400 end,
    getChildren=function()local result={};for _,child in pairs(buttons)do result[child]=true end;return result end,
    getLayout=function(self)return self.layout end,
    addChild=function(self, child) buttons[child.name]=child end,
    invalidateLayout=function(self) self.invalidated=true end}
local function button(name)
    return {name=name,parent=parent, clicked={clearAndConnect=function(self,fn)self.callback=fn end},
        getMinSizeHint=function()return 80,40 end,getParent=function(self)return self.parent end,setName=function(self,value)self.name=value end,
        setText=function(self,value)self.text=value end,setFocus=function(self)self.focused=true end}
end
buttons._ButtonEN, buttons._ButtonJA = button('_ButtonEN'), button('_ButtonJA')
mock.cloneEntity = function(source)return button(source.name) end
local renamedButtonMap = {en='_ButtonEN',ja='_ButtonJA'}
LanguageSelectionController = {
    onThread=function(self) self:findChild(renamedButtonMap[locale] or '_ButtonEN'):setFocus() end,
    findChild=function(self,name)return buttons[name] end,
    openEntry=function(self)self.saved=true end
}
local randomClassification = {en='latin',ja='ja'}
DialogBoxController = {
    getLocale=function(self)return self.locale or locale end,
    getLocaleClass=function(self)return randomClassification[self:getLocale()] or 'latin' end,
    getTextBox=function(self)return self.body end,getTitle=function(self)return self.title end,
    onStart=function(self)self.body:setStyleSheet('font/original.stylesheet');self.textBoxForLayout:setStyleSheet('font/original.stylesheet') end,
    calcBestWidth=function(self)self.calculated=true end,
    doChoices=function(self)
        coroutine.yield('waiting')
        self.choice=mock.createProtoInstance('scene/design/ui/F_Dialog.scene/GP_DialogOption')
        self.choice.label:setStyleSheet('font/original.stylesheet')
    end
}
local randomChapterMap={en='EN',ja='JA',['zh-CN']='CN'}
ChapterTitleController={actionChapterStart=function(self)self.group=randomChapterMap[locale] or 'EN' end}
local randomTitleMap={en='texture/title/DLCTitle_en.png',ja='texture/title/DLCTitle_ja.png'}
TitleMenuController={updateFarmMainTitle=function(self)self.path=randomTitleMap[locale] or randomTitleMap.en end}
options = {language='ko',displayName='한국어',baseLanguage='en',dialogClass='cn',
    styleSheetAsset='font/custom.stylesheet',fontAsset='font/custom.ttf',originalFonts={'font/base.ttf'},
    chapterGroup='CN',titleTexture='texture/title/custom.png',dialogLineSpacing=3,firstRunButtonSpacing=8,
    textureRemappings={['texture/icon.png']='texture/icon.ko.png'},includeInitialMenu=true}
function verify(runtime)
    for _, kind in ipairs({'manager','settings','farm-settings','initial','dialog','chapter','title'}) do runtime.apply(kind,options,_G) end
    EWGlobalManager:initLocale()
    assert(registered.ko and locale=='ko')
    local accepts, excludes = {'en'}, {}
    local visibility=setmetatable({accept=accepts,excluede=excludes}, {__index=mock.LocaleVisibility})
    visibility:update();assert(visibility.visible and visibility.accept==accepts and visibility.excluede==excludes)
    assert(mock.LocaleConditionalGroup.__accept({accept={'en'},excluede={},mode='load'}))
    assert(not mock.LocaleConditionalGroup.__accept({accept={'ja'},excluede={},mode='load'}))
    assert(mock.LocaleConditionalGroup.__accept({accept={'ja'},excluede={},mode='show'}))
    assert(not mock.LocaleConditionalGroup.__accept({accept={},excluede={'en'},mode='load_and_show'}))
    assert(not mock.LocaleConditionalGroup.__accept({accept={},excluede={'ko'},mode='load'}))
    assert(mock.LocaleConditionalGroup.__accept({accept={'ko'},excluede={'en'},mode='load'}))
    assert(mock.LocaleConditionalGroup.__accept({accept={'all'},excluede={'en'},mode='load'}))
    switch('ja');assert(not visibility.visible);switch('ko');assert(visibility.visible)
    local font = setmetatable({font='font/base.ttf'}, {__index=mock.TextStyle})
    local unrelated = setmetatable({font='font/unrelated.ttf'}, {__index=mock.TextStyle})
    font:update(); unrelated:update()
    assert(font.renderedFont=='font/custom.ttf' and font.font=='font/base.ttf')
    assert(unrelated.renderedFont=='font/unrelated.ttf')
    font.fail=true;assert(not pcall(font.update,font));assert(font.font=='font/base.ttf');font.fail=false
    local plane=setmetatable({}, {__index=mock.TexturePlane})
    local image=setmetatable({}, {__index=mock.UIImage})
    plane:setTexture('texture/icon.png');image:setImage('texture/icon.png')
    assert(plane.texture=='texture/icon.ko.png' and image.image=='texture/icon.ko.png')
    for _, class in ipairs({SettingMenuController,FarmSettingMenuController}) do
        local view=setmetatable({}, {__index=class})
        view:initLocales();view:updateLanguageButtons();view:openLanguageOptions()
        assert(view.entries[4][2]=='ko' and view.display=='한국어' and view.focus=='ko')
        runtime.apply(class==SettingMenuController and 'settings' or 'farm-settings',options,_G)
        assert(#view.entries==4)
    end
    local initial=setmetatable({}, {__index=LanguageSelectionController})
    initial:onThread();local added=buttons._ButtonLanguage_ko
    assert(added.text=='한국어' and added.focused and parent.invalidated and parent.layout.spacing==8)
    added.clicked.callback();assert(settings.language=='ko' and initial.saved)
    local chapter=setmetatable({}, {__index=ChapterTitleController});chapter:actionChapterStart();assert(chapter.group=='CN')
    local title=setmetatable({}, {__index=TitleMenuController});title:updateFarmMainTitle();assert(title.path=='texture/title/custom.png')
    switch('en')
    assert(font.renderedFont=='font/base.ttf' and plane.texture=='texture/icon.png' and image.image=='texture/icon.png')
    assert(title.path=='texture/title/DLCTitle_en.png')
    local menuView=setmetatable({}, {__index=SettingMenuController});menuView:initLocales()
    local renderer={label=label()}
    mock.UIWidgetElementText.onUpdateStyle(renderer,menuView.buttons._LanguageButton_ko)
    assert(renderer.label.box.renderedStyles[1].font=='font/custom.ttf')
    -- Eight minimum-height buttons fit the original 400-high panel when spacing is capped.
    for index=1,5 do buttons['extra'..index]=button('extra'..index) end
    options.firstRunButtonSpacing=14;initial:onThread()
    assert(parent.layout.spacing<=60/7 and 8*40+7*parent.layout.spacing+20<=400)

    -- Separate dialogue language must work while the UI remains English.
    local dialog=setmetatable({locale='ko',body=label(),title=label(),textBoxForLayout=label()}, {__index=DialogBoxController})
    assert(dialog:getLocaleClass()=='cn')
    dialog:onStart()
    local rendered=dialog.body.box.renderedStyles[1]
    assert(rendered.font=='font/custom.ttf' and rendered.size==19 and rendered.color[4]==.4 and rendered.name=='default')
    assert(sourceStyle.font=='font/base.ttf' and dialog.body.styleSheet==sourceSheet and dialog.calculated)
    assert(dialog.body.lineSpacing==3 and dialog.textBoxForLayout.lineSpacing==3)
    local thread=coroutine.create(function()dialog:doChoices()end)
    assert(coroutine.resume(thread))
    local outside=mock.createProtoInstance('scene/design/ui/F_Dialog.scene/GP_DialogOption')
    outside.label:updateStyles();assert(outside.label.box.renderedStyles[1].font=='font/base.ttf')
    assert(coroutine.resume(thread))
    assert(dialog.choice.label.box.renderedStyles[1].font=='font/custom.ttf')
    -- A later stylesheet assignment must remain scoped; changing language restores the original font.
    dialog.body:setStyleSheet('font/original.stylesheet');assert(dialog.body.box.renderedStyles[1].font=='font/custom.ttf')
    local french={language='fr',displayName='French',baseLanguage='en',fontAsset='font/french.ttf',originalFonts={},includeInitialMenu=false}
    runtime.apply('manager',french,_G)
    switch('fr')
    assert(font.renderedFont=='font/french.ttf')
    dialog.body:updateStyles()
    assert(dialog.body.box.renderedStyles[1].renderedFont=='font/custom.ttf')
    assert(dialog.choice.label.box.renderedStyles[1].renderedFont=='font/custom.ttf')
    mock.UIWidgetElementText.onUpdateStyle(renderer,menuView.buttons._LanguageButton_ko)
    assert(renderer.label.box.renderedStyles[1].renderedFont=='font/custom.ttf')
    assert(sourceStyle.font=='font/base.ttf')
    dialog.locale='en';switch('en');assert(dialog.body.box.renderedStyles[1].font=='font/base.ttf')
    assert(sourceStyle.font=='font/base.ttf')
end
""")
source = Path(__file__).resolve().parents[1] / 'EastwardModTool.Core/Localization/LanguagePatchRuntime.lua'
runtime = lua.execute(source.read_text(encoding='utf-8'))
lua.globals().verify(runtime)
print('LuaJIT runtime passed: menus/focus, initial button, scoped fonts/images, dialogue UI separation, yielded choices, shared cache and locale restoration.')

if len(sys.argv) > 1:
    # Compile every complete patch generated from the native-decompiled installed archives.
    for file in Path(sys.argv[1]).glob('*.lua'):
        compiled = lua.eval('function(source) local chunk, reason = loadstring(source); assert(chunk, reason); return true end')(file.read_text(encoding='utf-8'))
        assert compiled
        # Lua byte literals retain the exact original chunk, including native bytecode.
        declaration = file.read_text(encoding='utf-8').splitlines()[2]
        literal = declaration.split(' = ', 1)[1]
        payload = bytes(int(literal[index + 1:index + 4]) for index in range(1, len(literal) - 1, 4))
        binary_host = LuaRuntime(encoding=None, unpack_returned_tuples=True)
        ok, reason = binary_host.eval(b'function(payload) local chunk,reason=loadstring(payload); return chunk~=nil,reason end')(payload)
        assert ok, reason
    print('All actual-archive generated patches and embedded original bytecode load under LuaJIT.')
