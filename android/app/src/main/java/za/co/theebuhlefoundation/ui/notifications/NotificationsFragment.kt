// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.notifications

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.fragment.app.viewModels
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.recyclerview.widget.LinearLayoutManager
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.R
import za.co.theebuhlefoundation.data.api.dto.NotificationItem
import za.co.theebuhlefoundation.databinding.FragmentListBinding
import za.co.theebuhlefoundation.databinding.ItemNotificationBinding
import za.co.theebuhlefoundation.ui.common.BindingAdapter
import za.co.theebuhlefoundation.ui.common.ContainerViewModelFactory
import za.co.theebuhlefoundation.ui.common.Format
import za.co.theebuhlefoundation.ui.common.StateViews
import za.co.theebuhlefoundation.ui.common.container

class NotificationsFragment : Fragment() {

    private var _binding: FragmentListBinding? = null
    private val binding get() = _binding!!

    private lateinit var state: StateViews

    private val viewModel: NotificationsViewModel by viewModels {
        ContainerViewModelFactory(container) { NotificationsViewModel(it.messages) }
    }

    private val adapter = BindingAdapter<NotificationItem, ItemNotificationBinding>(
        inflate = ItemNotificationBinding::inflate,
        identity = { it.id },
    ) { binding, item, _ ->
        binding.notificationTitle.text = item.title
        binding.notificationBody.text = item.body
        binding.notificationWhen.text = Format.date(item.createdAt)
        binding.unreadDot.visibility = if (item.readAt == null) View.VISIBLE else View.INVISIBLE

        binding.root.setOnClickListener {
            if (item.readAt == null) viewModel.markRead(item.id)
        }
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        parent: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentListBinding.inflate(inflater, parent, false)

        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        state = StateViews(binding.stateHost, binding.content)
        state.onRetry { viewModel.load() }

        binding.listTitle.text = getString(R.string.nav_alerts)
        binding.list.layoutManager = LinearLayoutManager(requireContext())
        binding.list.adapter = adapter
        binding.refresh.setOnRefreshListener { viewModel.load(refreshing = true) }

        binding.listAction.text = getString(R.string.notifications_mark_all)
        binding.listAction.setOnClickListener { viewModel.markAllRead() }

        viewLifecycleOwner.lifecycleScope.launch {
            viewLifecycleOwner.repeatOnLifecycle(Lifecycle.State.STARTED) {
                launch {
                    viewModel.state.collect { current ->
                        binding.refresh.isRefreshing = false
                        state.render(current, adapter::submitList)
                    }
                }

                launch {
                    // The button is only there when it would do something.
                    viewModel.unread.collect { unread ->
                        binding.listAction.visibility =
                            if (unread > 0) View.VISIBLE else View.GONE
                    }
                }
            }
        }

        viewModel.load()
    }

    override fun onDestroyView() {
        super.onDestroyView()
        binding.list.adapter = null
        _binding = null
    }
}
