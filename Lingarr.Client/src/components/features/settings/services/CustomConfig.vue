<template>
    <div class="flex flex-col space-y-2">
        <InputComponent
            v-model="address"
            validation-type="url"
            label="Address"
            @update:validation="(val) => (isValid.address = val)" />
    </div>
</template>

<script setup lang="ts">
import { computed, reactive } from 'vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts'
import InputComponent from '@/components/common/InputComponent.vue'

const settingsStore = useSettingStore()
const emit = defineEmits(['save'])
const isValid = reactive({
    address: false
})

const address = computed({
    get: () => settingsStore.getSetting(SETTINGS.CUSTOM_ENDPOINT) as string,
    set: (newValue: string) => {
        settingsStore.updateSetting(SETTINGS.CUSTOM_ENDPOINT, newValue, isValid.address)
        if (isValid.address) {
            emit('save')
        }
    }
})
</script>
