import { PotokSDK } from 'potok-sdk';
import { state } from './state.js';
import { buildPage } from './ui.js';
import { openMedia } from './actions.js';
import { mediaFromMediaActionProps } from './media.js';

await PotokSDK.storage.local.setItem('disableHttpProxy', true);

PotokSDK.registerSlotContribution({
    id: 'the-naked-gun',
    slotName: 'extension-page',
    render() {
        return {
            label: 'The Naked Gun',
            layout: buildPage()
        };
    }
});

PotokSDK.registerSlotContribution({
    id: 'the-naked-gun-media-actions',
    slotName: 'media-actions',
    render(props) {
        return {
            label: 'The Naked Gun',
            layout: PotokSDK.ui.components
                .Button('The Naked Gun')
                .variant('watch-online')
                .onClick(() => {
                    openMedia(mediaFromMediaActionProps(props));
                })
        };
    }
});

state.$subscribe(() => {
    PotokSDK.ui.render(buildPage(), 'the-naked-gun');
});